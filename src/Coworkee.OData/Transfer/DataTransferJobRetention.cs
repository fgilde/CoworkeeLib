using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Transfer;

internal sealed class DataTransferJobRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "data-transfer-jobs";

    public string Title => "Import and export jobs";

    public string? Description => "Finished imports and exports with their uploaded and result files; an import can no longer be undone.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(j => j.FinishedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<DataTransferJob> Older(DateTimeOffset before) =>
        db.Set<DataTransferJob>().IgnoreQueryFilters().Where(j => j.FinishedAt != null && j.FinishedAt < before);
}
