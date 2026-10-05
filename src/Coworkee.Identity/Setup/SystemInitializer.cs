using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Setup;

internal sealed class SystemInitializer(CoworkeeDbContext db, UserManager<User> users, TimeProvider clock, IEnumerable<ISetupStep> steps)
{
    public async Task<Result<SetupResultDto>> InitializeAsync(CompleteSetupRequest request, CancellationToken cancellationToken)
    {
        var tenant = new Tenant { Name = request.TenantName, Identifier = "default", IsDefault = true };
        db.Add(tenant);
        var admin = await SystemRoleAsync(SystemRoles.Admin, "Full access", cancellationToken);
        await SystemRoleAsync(SystemRoles.User, "Signed-in user", cancellationToken);

        var user = new User
        {
            TenantId = tenant.Id,
            UserName = request.AdminEmail,
            Email = request.AdminEmail,
            EmailConfirmed = true,
            FirstName = request.AdminFirstName,
            LastName = request.AdminLastName,
        };

        using (CurrentUserScope.Begin(new ImpersonatedUser(null, tenant.Id)))
        {
            var created = await users.CreateAsync(user, request.AdminPassword);
            if (!created.Succeeded)
            {
                return IdentityErrors.ToError(created);
            }
        }

        db.Set<IdentityUserRole<Guid>>().Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = admin.Id });
        using (CurrentUserScope.Begin(new ImpersonatedUser(user.Id, tenant.Id)))
        {
            foreach (var step in steps)
            {
                if (await step.ApplyAsync(request, tenant.Id, cancellationToken) is { } failed)
                {
                    return failed;
                }
            }
        }

        var systemState = await db.Set<SystemState>().FindAsync([SystemState.SingletonId], cancellationToken);
        if (systemState is null)
        {
            systemState = new SystemState();
            db.Add(systemState);
        }

        systemState.IsInitialized = true;
        systemState.InitializedAt = clock.GetUtcNow();
        return new SetupResultDto(tenant.Id, user.Id);
    }

    private async Task<Role> SystemRoleAsync(string name, string description, CancellationToken cancellationToken)
    {
        var normalized = name.ToUpperInvariant();
        var role = await db.Set<Role>().FirstOrDefaultAsync(r => r.IsSystem && r.NormalizedName == normalized, cancellationToken);
        if (role is null)
        {
            role = new Role { Name = name, NormalizedName = normalized, Description = description, IsSystem = true };
            db.Add(role);
        }

        return role;
    }
}

