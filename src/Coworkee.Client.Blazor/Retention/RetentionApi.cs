using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Retention;

namespace Coworkee.Client.Blazor.Retention;

internal sealed class RetentionApi(HttpClient http) : ApiClientBase(http), IRetentionApi
{
    private const string Root = "api/v1/retention";

    public async Task<IReadOnlyList<RetentionTargetDto>> GetAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<RetentionTargetDto[]>(Root, cancellationToken);

    public Task<long> CountAsync(string key, int days, CancellationToken cancellationToken = default) =>
        GetAsync<long>($"{Root}/{Uri.EscapeDataString(key)}/count?days={days}", cancellationToken);

    public Task SetDaysAsync(IReadOnlyDictionary<string, int?> days, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, Root, days, cancellationToken);

    public Task RunAsync(string key, int days, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/{Uri.EscapeDataString(key)}/run", new RunRetentionRequest(days), cancellationToken);
}
