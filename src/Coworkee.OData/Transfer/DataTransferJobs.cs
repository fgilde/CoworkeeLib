using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Transfer;

/// <summary>The imports and exports of the current user, newest first, and their result files.</summary>
public sealed class DataTransferJobs(CoworkeeDbContext db, ODataEntityRegistry registry, ICurrentUser user)
{
    private const int Recent = 50;

    public async Task<IReadOnlyList<DataTransferJobDto>> ListAsync(string? entitySet, CancellationToken cancellationToken)
    {
        var jobs = await Mine()
            .Where(j => j.Status != DataTransferStatus.Draft && (entitySet == null || j.EntitySet == entitySet))
            .OrderByDescending(j => j.CreatedAt)
            .Take(Recent)
            .Select(Projection)
            .ToListAsync(cancellationToken);
        var undoable = await UndoableAsync(cancellationToken);
        return [.. jobs.Select(j => Dto(j, undoable))];
    }

    public async Task<Result<DataTransferJobDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Mine().Where(j => j.Id == id).Select(Projection).SingleOrDefaultAsync(cancellationToken) is { } job
            ? Dto(job, await UndoableAsync(cancellationToken))
            : Error.NotFound("data_transfer.not_found", "There is no such import or export.");

    public async Task<Result<(byte[] Content, string ContentType, string Name)>> FileAsync(Guid id, CancellationToken cancellationToken) =>
        await Mine().Where(j => j.Id == id && j.Result != null).Select(j => new { j.Result, j.ResultContentType, j.ResultFileName }).SingleOrDefaultAsync(cancellationToken) is { } file
            ? (file.Result!, file.ResultContentType ?? "application/octet-stream", file.ResultFileName ?? "result")
            : Error.NotFound("data_transfer.no_file", "This import or export has no file.");

    private IQueryable<DataTransferJob> Mine() => db.Set<DataTransferJob>().AsNoTracking().Where(j => j.UserId == user.UserId);

    // the last finished import per set may be undone, when it created rows and the set can delete them
    private async Task<HashSet<Guid>> UndoableAsync(CancellationToken cancellationToken)
    {
        var sets = registry.Imports.Where(i => i.Delete is not null).Select(i => i.EntitySet).ToList();
        var last = await Mine()
            .Where(j => j.Kind == DataTransferKind.Import && !j.DryRun && sets.Contains(j.EntitySet)
                        && j.Status != DataTransferStatus.Draft && j.Status != DataTransferStatus.Failed)
            .GroupBy(j => j.EntitySet)
            .Select(g => g.OrderByDescending(j => j.CreatedAt).Select(j => new { j.Id, j.Status, j.Created }).First())
            .ToListAsync(cancellationToken);
        return [.. last.Where(j => j.Status == DataTransferStatus.Completed && j.Created > 0).Select(j => j.Id)];
    }

    private static readonly System.Linq.Expressions.Expression<Func<DataTransferJob, JobRow>> Projection = j => new JobRow(
        j.Id, j.Kind, j.EntitySet, j.Status, j.DryRun, j.Total, j.Processed, j.Created, j.Updated, j.Failed, j.FileName, j.ResultFileName, j.Message, j.CreatedAt, j.FinishedAt);

    private static DataTransferJobDto Dto(JobRow j, HashSet<Guid> undoable) => new(
        j.Id, j.Kind, j.EntitySet, j.Status, j.DryRun, j.Total, j.Processed, j.Created, j.Updated, j.Failed, j.FileName, j.ResultFileName, j.Message, j.CreatedAt, j.FinishedAt,
        undoable.Contains(j.Id));

    private sealed record JobRow(
        Guid Id, DataTransferKind Kind, string EntitySet, DataTransferStatus Status, bool DryRun, int Total, int Processed, int Created, int Updated, int Failed,
        string? FileName, string? ResultFileName, string? Message, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
}
