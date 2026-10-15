using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Auditing;

namespace Coworkee.Client.Blazor.History;

internal sealed class EntityHistoryApi(HttpClient http) : ApiClientBase(http), IEntityHistoryApi
{
    private const string Root = "api/v1/history";

    public Task<EntityHistoryDto> GetAsync(string entitySet, Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<EntityHistoryDto>($"{Root}/{Uri.EscapeDataString(entitySet)}/{id}", cancellationToken);

    public Task<EntityHistoryPreviewDto> PreviewAsync(string entitySet, Guid id, EntityHistoryRestoreRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<EntityHistoryPreviewDto>(HttpMethod.Post, $"{Root}/{Uri.EscapeDataString(entitySet)}/{id}/preview", request, cancellationToken);

    public Task RestoreAsync(string entitySet, Guid id, EntityHistoryRestoreRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/{Uri.EscapeDataString(entitySet)}/{id}/restore", request, cancellationToken);

    public async Task<IReadOnlyList<DeletedRecordDto>> GetDeletedAsync(string entitySet, CancellationToken cancellationToken = default) =>
        await GetAsync<DeletedRecordDto[]>($"{Root}/{Uri.EscapeDataString(entitySet)}/deleted", cancellationToken);
}
