using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.ApiKeys;

internal sealed class ApiKeyPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "apiKeys";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Mine(subject).AsNoTracking().OrderBy(k => k.CreatedAt)
            .Select(k => new { k.Name, k.Description, k.Prefix, k.Scopes, k.CreatedAt, k.ExpiresAt, k.LastUsedAt, k.LastUsedIp, k.RevokedAt })
            .ToListAsync(cancellationToken);

    /// <summary>His keys go; keys of others he revoked keep the revocation, without his id.</summary>
    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        await Mine(subject).ExecuteDeleteAsync(cancellationToken);
        var erased = (Guid?)PersonalDataSubject.ErasedUserId;
        await db.Set<ApiKey>().IgnoreQueryFilters().Where(k => k.RevokedBy == subject.UserId).ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedBy, erased), cancellationToken);
    }

    private IQueryable<ApiKey> Mine(PersonalDataSubject subject) => db.Set<ApiKey>().IgnoreQueryFilters().Where(k => k.UserId == subject.UserId);
}
