using Coworkee.Application.Messaging;
using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Setup;

public sealed record GetSetupStatus : IQuery<Result<SetupStatusDto>>;

public sealed record GetSetupChecks : IQuery<Result<IReadOnlyList<SetupCheckDto>>>;

public sealed record CompleteSetup(CompleteSetupRequest Request) : ICommand<Result<SetupResultDto>>;

internal sealed class CompleteSetupValidator : AbstractValidator<CompleteSetup>
{
    public CompleteSetupValidator()
    {
        RuleFor(c => c.Request.SetupToken).NotEmpty();
        RuleFor(c => c.Request.TenantName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Request.AdminEmail).NotEmpty().EmailAddress();
        RuleFor(c => c.Request.AdminPassword).NotEmpty().MinimumLength(8);
    }
}

internal sealed class GetSetupStatusHandler(SystemStateCache state) : IHandler<GetSetupStatus, Result<SetupStatusDto>>
{
    public async Task<Result<SetupStatusDto>> HandleAsync(GetSetupStatus request, CancellationToken cancellationToken) =>
        new SetupStatusDto(await state.IsInitializedAsync(cancellationToken));
}

internal sealed class GetSetupChecksHandler(SystemStateCache state, IEnumerable<ISetupCheck> checks) : IHandler<GetSetupChecks, Result<IReadOnlyList<SetupCheckDto>>>
{
    public async Task<Result<IReadOnlyList<SetupCheckDto>>> HandleAsync(GetSetupChecks request, CancellationToken cancellationToken)
    {
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Error.Conflict("setup.completed", "The system is already set up.");
        }

        var results = new List<SetupCheckDto>();
        foreach (var check in checks)
        {
            try
            {
                results.Add(await check.RunAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new SetupCheckDto(check.Name, SetupCheckStatus.Error, exception.Message));
            }
        }

        return results;
    }
}

internal sealed class DatabaseSetupCheck(CoworkeeDbContext db) : ISetupCheck
{
    public string Name => "Database";

    public async Task<SetupCheckDto> RunAsync(CancellationToken cancellationToken) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? new SetupCheckDto(Name, SetupCheckStatus.Ok, null)
            : new SetupCheckDto(Name, SetupCheckStatus.Error, "The database is not reachable.");
}

internal sealed class CompleteSetupHandler(
    CoworkeeDbContext db, UserManager<User> users, SetupToken token, SystemStateCache state, TimeProvider clock, IEnumerable<ISetupStep> steps)
    : IHandler<CompleteSetup, Result<SetupResultDto>>
{
    public async Task<Result<SetupResultDto>> HandleAsync(CompleteSetup command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Error.Conflict("setup.completed", "The system is already set up.");
        }

        if (!token.Matches(request.SetupToken))
        {
            return Error.Forbidden("setup.token_invalid", "The setup token is not valid.");
        }

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

internal static class IdentityErrors
{
    public static Error ToError(IdentityResult result) =>
        Error.Validation(result.Errors
            .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "Password"
                : e.Code.Contains("Email", StringComparison.Ordinal) || e.Code.Contains("UserName", StringComparison.Ordinal) ? "Email"
                : "User")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal));
}
