using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.People;

/// <summary>Who of the organisation is online, loaded once and kept live; <see cref="Changed"/> names the user who came or went.</summary>
public sealed class Presence(ICoworkeeApi api, RealtimeClient realtime) : IAsyncDisposable
{
    private Task<HashSet<Guid>>? _online;
    private IAsyncDisposable? _subscription;

    public event Action<Guid>? Changed;

    public async Task<bool> IsOnlineAsync(Guid userId) => (await (_online ??= LoadAsync())).Contains(userId);

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    // subscribes before it asks, so nobody who comes meanwhile is missed
    private async Task<HashSet<Guid>> LoadAsync()
    {
        _subscription = await realtime.SubscribeAsync(PresenceEvents.Topic, ReceiveAsync);
        try
        {
            return [.. await api.GetOnlineUsersAsync() ?? []];
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return [];
        }
    }

    private async Task ReceiveAsync(RealtimeEnvelope envelope)
    {
        if (envelope.Type != PresenceEvents.Changed || envelope.Payload.Deserialize<PresenceDto>(JsonSerializerOptions.Web) is not { } change
            || _online is null)
        {
            return;
        }

        var online = await _online;
        if (change.Online ? online.Add(change.UserId) : online.Remove(change.UserId))
        {
            Changed?.Invoke(change.UserId);
        }
    }
}
