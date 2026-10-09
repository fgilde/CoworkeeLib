using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Coworkee.AuthServer;

/// <summary>Sign-in grants and tokens; erasing them ends every session of the user.</summary>
internal sealed class AuthPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "signIns";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var id = subject.UserId.ToString();
        return await db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().AsNoTracking().Where(a => a.Subject == id).OrderBy(a => a.CreationDate)
            .Select(a => new { Client = a.Application!.DisplayName, a.Scopes, a.Status, a.CreationDate })
            .ToListAsync(cancellationToken);
    }

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var id = subject.UserId.ToString();
        await db.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().Where(t => t.Subject == id || t.Authorization!.Subject == id).ExecuteDeleteAsync(cancellationToken);
        await db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Where(a => a.Subject == id).ExecuteDeleteAsync(cancellationToken);
    }
}
