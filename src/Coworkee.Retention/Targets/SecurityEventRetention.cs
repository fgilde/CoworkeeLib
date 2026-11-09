using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention.Targets;

internal sealed class SecurityEventRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "security-events";

    public string Title => "Security log";

    public string? Description => "Sign-ins, lockouts and changes of passwords, keys and access.";

    public int? DefaultDays => 365;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(e => e.At).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<SecurityEvent> Older(DateTimeOffset before) => db.Set<SecurityEvent>().Where(e => e.At < before);
}
