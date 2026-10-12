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

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Mine(subject).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<ApiKey> Mine(PersonalDataSubject subject) => db.Set<ApiKey>().Where(k => k.UserId == subject.UserId);
}
