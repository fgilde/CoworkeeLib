using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>
/// The groups the signed-in user works in (<see cref="GroupScopePicker"/>). A new selection, here or in another tab of the user, drops the
/// client entities and raises <see cref="Changed"/>, on which data tables reload.
/// </summary>
public sealed class GroupScopeState(ICoworkeeApi api, RealtimeClient realtime, AuthenticationStateProvider authentication, IClientDataCache? cache = null) : IAsyncDisposable
{
    private Task? _subscribed;
    private IAsyncDisposable? _subscription;
    private HashSet<Guid> _applied = [];

    /// <summary>What the picker shows: the accessible groups; with ScopeAll only memberships, the selection and their ancestors.</summary>
    public IReadOnlyList<MyGroupDto> Groups { get; private set; } = [];

    /// <summary>The selection changed; lists, searches and exports follow it.</summary>
    public event Action? Changed;

    public async Task LoadAsync()
    {
        await (_subscribed ??= SubscribeAsync());
        try
        {
            Groups = await api.GetMyGroupsAsync();
            _applied = [.. Groups.Where(g => g.Selected).Select(g => g.Id)];
        }
        catch (ApiException)
        {
            // signed out or no identity API: the picker stays hidden
        }
    }

    /// <summary>Stores the selection; empty selects all of the user's groups. Throws <see cref="ApiException"/> for groups the user cannot access.</summary>
    public async Task SelectAsync(IReadOnlyList<Guid> groupIds)
    {
        // the server echoes the selection on the user's topic; this tab has it already
        _applied = [.. groupIds];
        await api.SelectGroupsAsync(groupIds);
        await ApplyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private async Task ApplyAsync()
    {
        await LoadAsync();
        if (cache is not null)
        {
            await cache.ClearAsync();
        }

        Changed?.Invoke();
    }

    private async Task SubscribeAsync()
    {
        if (ClientUser.From((await authentication.GetAuthenticationStateAsync()).User).TryGetUserId(out var userId))
        {
            _subscription = await realtime.SubscribeAsync(RealtimeTopics.User(userId), OnEventAsync);
        }
    }

    private Task OnEventAsync(RealtimeEnvelope envelope) =>
        envelope.Type == GroupSelectionEvents.Changed && !_applied.SetEquals(Ids(envelope.Payload)) ? ApplyAsync() : Task.CompletedTask;

    private static IEnumerable<Guid> Ids(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("groupIds", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Select(id => id.GetGuid())
            : [];
}
