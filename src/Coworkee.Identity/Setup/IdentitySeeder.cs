using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Setup;

/// <summary>Sets the system up without the wizard: the first admin of the options becomes the setup admin, then roles and the other users follow.</summary>
internal sealed class IdentitySeeder(
    CoworkeeDbContext db, UserManager<User> users, SystemStateCache state, SystemInitializer initializer, IOptions<IdentitySeedOptions> options)
{
    private readonly IdentitySeedOptions _options = options.Value;

    public async Task<Result> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Result.Success();
        }

        var admin = _options.Users.FirstOrDefault(u => u.IsAdmin)
            ?? throw new InvalidOperationException("The identity seed needs at least one admin user.");
        var setup = await initializer.InitializeAsync(
            new CompleteSetupRequest(string.Empty, _options.TenantName, admin.Email, admin.Password, admin.FirstName, admin.LastName), cancellationToken);
        if (!setup.IsSuccess)
        {
            return setup.Error!;
        }

        var (tenantId, adminId) = (setup.Value.TenantId, setup.Value.AdminUserId);
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(adminId, tenantId));
        var roles = _options.Roles.ToDictionary(r => r.Name, r => AddRole(r, tenantId), StringComparer.OrdinalIgnoreCase);
        var adminRole = db.Set<Role>().Local.First(r => r.IsSystem && r.Name == SystemRoles.Admin);
        foreach (var user in _options.Users.Where(u => u != admin))
        {
            if (await AddUserAsync(user, tenantId) is { } failed)
            {
                return failed;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        state.Reset();
        return Result.Success();

        async Task<Error?> AddUserAsync(SeedUser seed, Guid tenant)
        {
            var user = new User { TenantId = tenant, UserName = seed.Email, Email = seed.Email, EmailConfirmed = true, FirstName = seed.FirstName, LastName = seed.LastName };
            var created = await users.CreateAsync(user, seed.Password);
            if (!created.Succeeded)
            {
                return IdentityErrors.ToError(created);
            }

            var roleIds = seed.Roles.Select(name => roles[name].Id).Append(seed.IsAdmin ? adminRole.Id : Guid.Empty).Where(id => id != Guid.Empty);
            db.Set<IdentityUserRole<Guid>>().AddRange(roleIds.Select(id => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = id }));
            return null;
        }
    }

    private Role AddRole(SeedRole seed, Guid tenantId)
    {
        var role = new Role { Name = seed.Name, NormalizedName = seed.Name.ToUpperInvariant(), Description = seed.Description, TenantId = tenantId };
        db.Add(role);
        db.AddRange(seed.Permissions.Select(p => new PermissionGrant { TenantId = tenantId, Name = p, ProviderType = PermissionProviderType.Role, ProviderKey = role.Id }));
        return role;
    }
}
