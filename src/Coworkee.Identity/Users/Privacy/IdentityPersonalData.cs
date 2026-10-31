using Coworkee.Application.Privacy;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Privacy;

/// <summary>Profile, address, picture, memberships, claims and own grants; erasing deletes the user with all of it (the library blanks his audit trail afterwards).</summary>
internal sealed class IdentityPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
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
        var passkeys = await db.Set<IdentityUserPasskey<Guid>>().AsNoTracking().Where(p => p.UserId == user.Id).ToListAsync(cancellationToken);
        var claims = await db.Set<IdentityUserClaim<Guid>>().Where(c => c.UserId == user.Id).Select(c => new { c.ClaimType, c.ClaimValue }).ToListAsync(cancellationToken);
        var grants = await Grants(subject).AsNoTracking().OrderBy(g => g.Name).Select(g => g.Name).ToListAsync(cancellationToken);
        var resources = await (from permission in ResourceGrants(subject).AsNoTracking()
                               join role in db.Set<Role>().IgnoreQueryFilters() on permission.RoleId equals role.Id
                               select new { permission.ResourceType, permission.ResourceId, Role = role.Name }).ToListAsync(cancellationToken);
        var selected = await (from selection in db.Set<GroupSelection>()
                              join userGroup in db.Set<UserGroup>().IgnoreQueryFilters() on selection.GroupId equals userGroup.Id
                              where selection.UserId == user.Id
                              select userGroup.Name).ToListAsync(cancellationToken);
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
            Passkeys = passkeys.Select(p => new { p.Data.Name, p.Data.CreatedAt, p.Data.IsBackedUp }),
            Claims = claims,
            PermissionGrants = grants,
            ResourcePermissions = resources,
            SelectedGroups = selected,
        };
    }

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        await Grants(subject).ExecuteDeleteAsync(cancellationToken);
        await ResourceGrants(subject).ExecuteDeleteAsync(cancellationToken);

        // a direct delete writes no audit entry with the old values; roles, claims, logins, tokens, passkeys, memberships and group selections go with it by cascade
        await db.Set<User>().Where(u => u.Id == subject.UserId).ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<PermissionGrant> Grants(PersonalDataSubject subject) =>
        db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.User && g.ProviderKey == subject.UserId);

    private IQueryable<ResourcePermission> ResourceGrants(PersonalDataSubject subject) =>
        db.Set<ResourcePermission>().IgnoreQueryFilters().Where(p => p.PrincipalType == PrincipalType.User && p.PrincipalId == subject.UserId);
}
