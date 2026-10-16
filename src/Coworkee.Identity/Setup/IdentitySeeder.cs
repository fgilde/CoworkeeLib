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
            new CompleteSetupRequest(string.Empty, _options.TenantName, admin.Email, admin.Password, admin.FirstName, admin.LastName, _options.Settings), cancellationToken);
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

    /// <summary>
    /// Makes the accounts of an initialized system match the options: missing users and roles are added, seeded roles get the permissions added to
    /// the seed since, a user whose password differs gets the seeded one. Other users, roles and grants stay as they are; nothing is removed.
    /// </summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Set<Tenant>().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.IsDefault, cancellationToken) is not { } tenant)
        {
            return;
        }

        var adminRole = await db.Set<Role>().IgnoreQueryFilters()
            .FirstAsync(r => (r.TenantId == null || r.TenantId == tenant.Id) && r.IsSystem && r.Name == SystemRoles.Admin, cancellationToken);
        var actorId = await db.Set<IdentityUserRole<Guid>>().Where(r => r.RoleId == adminRole.Id).Select(r => r.UserId).FirstOrDefaultAsync(cancellationToken);
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(actorId == Guid.Empty ? null : actorId, tenant.Id));
        var roles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in _options.Users)
        {
            var email = seed.Email.ToUpperInvariant();
            var user = await db.Set<User>().IgnoreQueryFilters().FirstOrDefaultAsync(u => u.NormalizedEmail == email && u.TenantId == tenant.Id, cancellationToken);
            if (user is null)
            {
                await CreateAsync(seed);
            }
            else if (!await users.CheckPasswordAsync(user, seed.Password))
            {
                Ensure(await users.RemovePasswordAsync(user), seed);
                Ensure(await users.AddPasswordAsync(user, seed.Password), seed);
            }
        }

        await GrantMissingAsync(tenant.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        async Task CreateAsync(SeedUser seed)
        {
            foreach (var name in seed.Roles.Where(n => !roles.ContainsKey(n)))
            {
                roles[name] = await db.Set<Role>().IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == name, cancellationToken)
                    ?? AddRole(_options.Roles.First(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)), tenant.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            var user = new User { TenantId = tenant.Id, UserName = seed.Email, Email = seed.Email, EmailConfirmed = true, FirstName = seed.FirstName, LastName = seed.LastName };
            Ensure(await users.CreateAsync(user, seed.Password), seed);
            var roleIds = seed.Roles.Select(name => roles[name].Id).Concat(seed.IsAdmin ? [adminRole.Id] : []);
            db.Set<IdentityUserRole<Guid>>().AddRange(roleIds.Select(id => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = id }));
        }

        static void Ensure(IdentityResult result, SeedUser seed)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Seed user {seed.Email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }

    private async Task GrantMissingAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        foreach (var seed in _options.Roles)
        {
            var role = db.Set<Role>().Local.FirstOrDefault(r => r.TenantId == tenantId && r.Name == seed.Name)
                ?? await db.Set<Role>().IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == seed.Name, cancellationToken);
            if (role is null || db.Entry(role).State == EntityState.Added)
            {
                continue;
            }

            var granted = await db.Set<PermissionGrant>().IgnoreQueryFilters()
                .Where(g => g.TenantId == tenantId && g.ProviderType == PermissionProviderType.Role && g.ProviderKey == role.Id).Select(g => g.Name).ToListAsync(cancellationToken);
            db.AddRange(seed.Permissions.Except(granted).Select(p => new PermissionGrant { TenantId = tenantId, Name = p, ProviderType = PermissionProviderType.Role, ProviderKey = role.Id }));
        }
    }

    private Role AddRole(SeedRole seed, Guid tenantId)
    {
        var role = new Role
        {
            Name = seed.Name, NormalizedName = seed.Name.ToUpperInvariant(), Description = seed.Description, TenantId = tenantId, SelectableForRegistration = seed.SelectableForRegistration,
        };
        db.Add(role);
        db.AddRange(seed.Permissions.Select(p => new PermissionGrant { TenantId = tenantId, Name = p, ProviderType = PermissionProviderType.Role, ProviderKey = role.Id }));
        return role;
    }
}
