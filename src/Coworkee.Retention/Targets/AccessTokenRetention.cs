using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention.Targets;

/// <summary>Anonymous tokens are worthless once expired; they go a day later.</summary>
internal sealed class AccessTokenRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "access-tokens";

    public string Title => "Expired access tokens";

    public string? Description => "Temporary file links and export downloads whose time is up.";

    public int? DefaultDays => 1;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(t => t.ExpiresAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<AccessToken> Older(DateTimeOffset before) => db.Set<AccessToken>().Where(t => t.ExpiresAt < before);
}
