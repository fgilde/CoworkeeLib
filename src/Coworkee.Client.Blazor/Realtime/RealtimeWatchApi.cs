using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.Realtime;

internal sealed class RealtimeWatchApi(HttpClient http) : ApiClientBase(http), IRealtimeWatchApi
{
    private const string Root = "api/v1/realtime/watches";

    public async Task<IReadOnlyList<RealtimeWatchDto>> GetAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<RealtimeWatchDto[]>(Root, cancellationToken);

    public Task SetAsync(string entityType, bool enabled, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/{Uri.EscapeDataString(entityType)}", new SetRealtimeWatchRequest(enabled), cancellationToken);
}
