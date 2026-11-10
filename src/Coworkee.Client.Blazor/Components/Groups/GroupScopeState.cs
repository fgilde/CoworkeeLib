using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>
/// The groups the signed-in user works in (<see cref="GroupScopePicker"/>). A new selection, here or in another tab of the user, moves the
/// client entities to the copies of that selection and raises <see cref="Changed"/>, on which data tables reload. So does a change of the groups
/// the user may work in (an administrator adds or removes a membership, moves or deletes a group): the copies must not keep rows of a group the
/// user lost.
/// </summary>
public sealed class GroupScopeState(
    ICoworkeeApi api, RealtimeClient realtime, AuthenticationStateProvider authentication, ILogger<GroupScopeState> logger, IClientDataCache? cache = null)
    : IAsyncDisposable
{
    // a membership save touches several rows; one reload for the burst (as the permissions)
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);
    private readonly SemaphoreSlim _selecting = new(1, 1);
    private Task? _subscribing;
    private IAsyncDisposable? _subscription;
    private IAsyncDisposable? _accessSubscription;
    private CancellationTokenSource? _pending;
    private HashSet<Guid> _applied = [];
    private int _loads;

    /// <summary>What the picker shows: the accessible groups; with ScopeAll only memberships, the selection and their ancestors.</summary>
    public IReadOnlyList<MyGroupDto> Groups { get; private set; } = [];

    /// <summary>The selection changed; lists, searches and exports follow it.</summary>
    public event Action? Changed;

    public async Task LoadAsync()
    {
        // the hub may take its time: the groups do not wait for it; a failed subscription is tried again on the next load
        if (_subscription is null && _subscribing is not { IsCompleted: false })
        {
            _subscribing = SubscribeAsync();
        }

        var load = ++_loads;
        try
        {
            var groups = await api.GetMyGroupsAsync();
            if (load == _loads)
            {
                Groups = groups;
                _applied = Selection();
            }
        }
        catch (ApiException)
        {
            // signed out or no identity API: the picker stays hidden
        }
    }

    /// <summary>Stores the selection; empty selects all of the user's groups. Throws <see cref="ApiException"/> for groups the user cannot access.</summary>
    public async Task SelectAsync(IReadOnlyList<Guid> groupIds)
    {
        // one at a time: two quick clicks must not race their PUTs
        await _selecting.WaitAsync();
        try
        {
            // the server echoes the selection on the user's topic; this tab has it already
            _applied = [.. groupIds];
            try
            {
                await api.SelectGroupsAsync(groupIds);
            }
            catch
            {
                _applied = Selection();
                throw;
            }

            await ApplyAsync();
        }
        finally
        {
            _selecting.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pending?.Cancel();
        foreach (var subscription in new[] { _subscription, _accessSubscription }.OfType<IAsyncDisposable>())
        {
            await subscription.DisposeAsync();
        }
    }

    private HashSet<Guid> Selection() => [.. Groups.Where(g => g.Selected).Select(g => g.Id)];

    private async Task ApplyAsync()
    {
        await LoadAsync();
        await FollowAsync();
    }

    private async Task FollowAsync()
    {
        try
        {
            if (cache is not null)
            {
                await cache.ScopeChangedAsync();
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The client entities did not follow the new group selection");
        }

        Changed?.Invoke();
    }

    private async Task SubscribeAsync()
    {
        // memberships, groups and their roles publish on the organisation's permission topic (IdentityRealtimeTopics); only a change of what this user
        // may pick, or of the selection the server keeps of it, moves the copies
        _accessSubscription ??= await realtime.SubscribeAsync(RealtimeTopics.TenantPermissions, _ => AccessChangedAsync());
        if (ClientUser.From((await authentication.GetAuthenticationStateAsync()).User).TryGetUserId(out var userId)
            && await realtime.SubscribeAsync(RealtimeTopics.User(userId), OnEventAsync) is var subscription && RealtimeClient.IsActive(subscription))
        {
            _subscription = subscription;
        }

    }

    private async Task AccessChangedAsync()
    {
        _pending?.Cancel();
        var pending = _pending = new CancellationTokenSource();
        try
        {
            await Task.Delay(Settle, pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var before = Groups.Select(g => (g.Id, g.Selected)).ToHashSet();
        await LoadAsync();
        if (!before.SetEquals(Groups.Select(g => (g.Id, g.Selected))))
        {
            await FollowAsync();
        }
    }

    private Task OnEventAsync(RealtimeEnvelope envelope) =>
        envelope.Type == GroupSelectionEvents.Changed && !_applied.SetEquals(Ids(envelope.Payload)) ? ApplyAsync() : Task.CompletedTask;

    private static IEnumerable<Guid> Ids(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("groupIds", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Select(id => id.GetGuid())
            : [];
}
