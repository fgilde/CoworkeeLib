using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing.Requests;

internal sealed class RequestLogRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "request-log";

    public string Title => "Request log";

    public string? Description => "API requests with headers and bodies.";

    public int? DefaultDays => 30;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(e => e.At).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<RequestLogEntry> Older(DateTimeOffset before) => db.Set<RequestLogEntry>().Where(e => e.At < before);
}
