using System.Security.Claims;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.External;

/// <summary>Finds the user of an external sign-in: by the linked login, else by a verified email, else a new user when allowed.</summary>
public sealed class ExternalSignIn(UserManager<User> users, CoworkeeDbContext db, ITenantDirectory tenants, IOptions<AuthServerOptions> options)
{
    public async Task<Result<User>> FindOrCreateAsync(ExternalLoginInfo login, CancellationToken cancellationToken)
    {
        using var anyTenant = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await users.FindByLoginAsync(login.LoginProvider, login.ProviderKey) is { } linked)
        {
            return Active(linked);
        }

        // linking by address trusts the provider only when it vouches for the address
        if ((login.Principal.FindFirstValue(ClaimTypes.Email) ?? login.Principal.FindFirstValue("email")) is not { Length: > 0 } email
            || !string.Equals(login.Principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Forbidden("external.email_unverified", "The sign-in provider sent no verified email address.");
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            if (!options.Value.External.AutoProvision || await tenants.GetSystemTenantIdAsync(cancellationToken) is not { } tenantId)
            {
                return Error.Forbidden("external.unknown_user", $"There is no account for {email}.");
            }

            user = new User
            {
                TenantId = tenantId,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = login.Principal.FindFirstValue(ClaimTypes.GivenName),
                LastName = login.Principal.FindFirstValue(ClaimTypes.Surname),
            };
            using var tenant = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
            if (await users.CreateAsync(user) is { Succeeded: false } created)
            {
                return Error.Validation("User", string.Join(" ", created.Errors.Select(e => e.Description)));
            }
        }

        if (await users.AddLoginAsync(user, new UserLoginInfo(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName)) is { Succeeded: false } added)
        {
            return Error.Validation("Login", string.Join(" ", added.Errors.Select(e => e.Description)));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Active(user);
    }

    private static Result<User> Active(User user) =>
        user.IsActive ? user : Error.Forbidden("external.inactive", "This account is deactivated.");
}
