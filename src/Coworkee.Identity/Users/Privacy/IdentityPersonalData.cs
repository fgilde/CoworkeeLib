using Coworkee.Application.Privacy;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Privacy;

/// <summary>Profile, address, picture and memberships; erasing deletes the user and blanks the values in the user's own audit trail.</summary>
internal sealed class IdentityPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    private const string Erased = "\"***\"";

    public string Section => "profile";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == subject.UserId, cancellationToken);
        var roles = await UserRoles.ForAsync(db, [user.Id], cancellationToken);
        var groups = await (from member in db.Set<UserGroupMember>()
                            join userGroup in db.Set<UserGroup>().IgnoreQueryFilters() on member.GroupId equals userGroup.Id
                            where member.UserId == user.Id
                            select userGroup.Name).ToListAsync(cancellationToken);
        var logins = await db.Set<IdentityUserLogin<Guid>>().Where(l => l.UserId == user.Id).Select(l => l.LoginProvider).ToListAsync(cancellationToken);
        return new
        {
            user.Id,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            Address = new PostalAddress(user.Street, user.ZipCode, user.City, user.Country),
            Picture = user.AvatarUrl,
            user.TwoFactorEnabled,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            Roles = roles.GetValueOrDefault(user.Id)?.Select(r => r.Name) ?? [],
            Groups = groups,
            ExternalLogins = logins,
        };
    }

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var id = subject.UserId.ToString();
        foreach (var entry in await db.Set<AuditEntry>().Where(e => e.EntityType == nameof(User) && e.EntityId == id).ToListAsync(cancellationToken))
        {
            var changes = entry.Changes.Select(c => new AuditChange
            {
                Property = c.Property,
                OldValue = c.OldValue is null ? null : Erased,
                NewValue = c.NewValue is null ? null : Erased,
            }).ToList();
            entry.Changes.Clear();
            entry.Changes.AddRange(changes);
        }

        await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.User && g.ProviderKey == subject.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<ResourcePermission>().IgnoreQueryFilters()
            .Where(p => p.PrincipalType == PrincipalType.User && p.PrincipalId == subject.UserId).ExecuteDeleteAsync(cancellationToken);

        // a direct delete writes no audit entry with the old values; roles, claims, logins, tokens and memberships go with it by cascade
        await db.Set<User>().Where(u => u.Id == subject.UserId).ExecuteDeleteAsync(cancellationToken);
    }
}
