using Coworkee.Contracts.Auditing;

namespace Coworkee.Client.Blazor.History;

public interface IEntityHistoryApi
{
    Task<EntityHistoryDto> GetAsync(string entitySet, Guid id, CancellationToken cancellationToken = default);

    Task<EntityHistoryPreviewDto> PreviewAsync(string entitySet, Guid id, EntityHistoryRestoreRequest request, CancellationToken cancellationToken = default);

    Task RestoreAsync(string entitySet, Guid id, EntityHistoryRestoreRequest request, CancellationToken cancellationToken = default);

    /// <summary>The sets with a history the user may read, with whether the user may restore them.</summary>
    Task<IReadOnlyList<EntityHistorySetDto>> GetSetsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeletedRecordDto>> GetDeletedAsync(string entitySet, CancellationToken cancellationToken = default);
}
