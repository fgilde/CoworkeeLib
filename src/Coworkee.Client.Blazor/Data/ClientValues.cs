using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Data;

/// <summary>
/// Free values of the client under a key (<c>/api/v1/client-values</c>): the user's own, else the organisation's global value. Read once and kept live:
/// a change by this user (in any tab or device) or a new global value raises <see cref="Changed"/>. Strings are stored as they are, the generic members
/// read and write JSON.
/// </summary>
public interface IClientValues
{
    /// <summary>A key changed; the argument is what applies now (<see cref="ClientValueDto.Value"/> null: no value left).</summary>
    event Action<ClientValueDto>? Changed;

    /// <summary>Every value the user sees, ordered by key.</summary>
    Task<IReadOnlyList<ClientValueDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The value of a key as stored, or null.</summary>
    Task<ClientValueDto?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>The value of a key read as JSON, or the default of <typeparamref name="T"/>.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores the text as it is; <see cref="ClientValueScope.Global"/> needs <see cref="SettingsPermissions.ManageGlobalClientValues"/>.</summary>
    Task SetAsync(string key, string value, ClientValueScope scope = ClientValueScope.User, CancellationToken cancellationToken = default);

    /// <summary>Stores the value as JSON.</summary>
    Task SetAsync<T>(string key, T value, ClientValueScope scope = ClientValueScope.User, CancellationToken cancellationToken = default);

    /// <summary>Deletes the user's own value (a global one applies again) or, with <see cref="ClientValueScope.Global"/>, the global value.</summary>
    Task DeleteAsync(string key, ClientValueScope scope = ClientValueScope.User, CancellationToken cancellationToken = default);
}

/// <summary>The HTTP calls of <see cref="ClientValues"/>.</summary>
public sealed class ClientValuesApi(HttpClient http) : ApiClientBase(http)
{
    public const string Url = "api/v1/client-values";

    public async Task<IReadOnlyList<ClientValueDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ClientValueDto[]>(Url, cancellationToken);

    public Task SetAsync(SetClientValueRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, Url, request, cancellationToken);

    public Task DeleteAsync(string key, ClientValueScope scope, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Url}?key={Uri.EscapeDataString(key)}&scope={scope}", null, cancellationToken);
}

internal sealed class ClientValues(ClientValuesApi api, RealtimeClient realtime, AuthenticationStateProvider authentication) : IClientValues
{
    private const string EntityType = "ClientValue";
    private Task<IReadOnlyList<ClientValueDto>>? _values;
    private Task? _reload;
    private bool _again;
    private bool _following;

    public event Action<ClientValueDto>? Changed;

    public Task<IReadOnlyList<ClientValueDto>> GetAllAsync(CancellationToken cancellationToken = default) => ValuesAsync(cancellationToken);

    public async Task<ClientValueDto?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        (await ValuesAsync(cancellationToken)).FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.Ordinal));

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        await FindAsync(key, cancellationToken) is { Value: { } json } ? JsonSerializer.Deserialize<T>(json, CoworkeeJson.Web) : default;

    public async Task SetAsync(string key, string value, ClientValueScope scope = ClientValueScope.User, CancellationToken cancellationToken = default)
    {
        await api.SetAsync(new SetClientValueRequest(key, value, scope), cancellationToken);
        await AfterWriteAsync();
    }

    public Task SetAsync<T>(string key, T value, ClientValueScope scope = ClientValueScope.User, CancellationToken cancellationToken = default) =>
        SetAsync(key, JsonSerializer.Serialize(value, CoworkeeJson.Web), scope, cancellationToken);

    public async Task DeleteAsync(string key, ClientValueScope scope = ClientValueScope.User, CancellationToken cancellationToken = default)
    {
        await api.DeleteAsync(key, scope, cancellationToken);
        await AfterWriteAsync();
    }

    // one request for every reader; the load does not follow one caller's token
    private async Task<IReadOnlyList<ClientValueDto>> ValuesAsync(CancellationToken cancellationToken)
    {
        var load = _values ??= LoadAsync();
        try
        {
            return await load.WaitAsync(cancellationToken);
        }
        catch when (load.IsFaulted)
        {
            // a failed load is tried again by the next reader
            if (ReferenceEquals(_values, load))
            {
                _values = null;
            }

            throw;
        }
    }

    private Task<IReadOnlyList<ClientValueDto>> LoadAsync()
    {
        // not awaited: a slow realtime hub must not hold up the page
        _ = FollowAsync();
        return api.GetAllAsync(CancellationToken.None);
    }

    private async Task FollowAsync()
    {
        if (_following)
        {
            return;
        }

        _following = true;
        try
        {
            await realtime.SubscribeAsync(SettingTopics.ClientValues, _ => ReloadAsync());
            if (ClientUser.From((await authentication.GetAuthenticationStateAsync()).User).TryGetUserId(out var userId))
            {
                // the user's topic carries other events too (notifications, settings)
                await realtime.SubscribeAsync(RealtimeTopics.User(userId),
                    envelope => EntityChangedPayload.From(envelope) is { EntityType: EntityType } ? ReloadAsync() : Task.CompletedTask);
            }
        }
        catch (Exception)
        {
            // without live updates the values still load and save
        }
    }

    // nothing loaded yet: the next read loads the new state anyway
    // ponytail: the own write is read back once here and once more on its realtime event (no change then, no event); skip one if writes get frequent
    private Task AfterWriteAsync() => _values is null ? Task.CompletedTask : ReloadAsync();

    // a burst of events: one reload, and one more for events during it
    private Task ReloadAsync()
    {
        if (_reload is { IsCompleted: false })
        {
            _again = true;
            return _reload;
        }

        return _reload = ReloadUntilQuietAsync();
    }

    private async Task ReloadUntilQuietAsync()
    {
        do
        {
            _again = false;
            var before = _values is { IsCompletedSuccessfully: true } loaded ? loaded.Result : null;
            IReadOnlyList<ClientValueDto> now;
            try
            {
                now = await api.GetAllAsync(CancellationToken.None);
            }
            catch (Exception e) when (e is ApiException or HttpRequestException)
            {
                return;
            }

            _values = Task.FromResult(now);
            if (before is not null)
            {
                Raise(before, now);
            }
        }
        while (_again);
    }

    private void Raise(IReadOnlyList<ClientValueDto> before, IReadOnlyList<ClientValueDto> now)
    {
        var old = before.ToDictionary(v => v.Key, StringComparer.Ordinal);
        var keys = now.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var value in now.Where(v => !old.TryGetValue(v.Key, out var previous) || previous != v))
        {
            Changed?.Invoke(value);
        }

        foreach (var gone in before.Where(v => !keys.Contains(v.Key)))
        {
            Changed?.Invoke(new ClientValueDto(gone.Key, null, false));
        }
    }
}
