using System.Security.Claims;
using Coworkee.AuthServer.Registration;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using static Coworkee.AuthServer.AuthTexts;

namespace Coworkee.AuthServer.External;

/// <summary>
/// Finds the user of an external sign-in: by the linked login, else by a verified email (linking the login), else a new user when
/// registration allows the address.
/// </summary>
public sealed class ExternalSignIn(
    UserManager<User> users, CoworkeeDbContext db, ITenantDirectory tenants, ISettingProvider settings, AccountRegistration registration,
    IOptions<AuthServerOptions> options, IOptions<RegistrationOptions> registrationOptions)
{
    public async Task<Result<User>> FindOrCreateAsync(ExternalLoginInfo login, CancellationToken cancellationToken)
    {
        using var anyTenant = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await users.FindByLoginAsync(login.LoginProvider, login.ProviderKey) is { } linked)
        {
            return Allowed(linked);
        }

        // linking by address trusts the provider only when it vouches for the address or is configured as trusted
        if ((login.Principal.FindFirstValue(ClaimTypes.Email) ?? login.Principal.FindFirstValue("email")) is not { Length: > 0 } email || !Verified(login))
        {
            return Error.Forbidden("external.email_unverified", T("The sign-in provider sent no verified email address."));
        }

        if (!Wildcards.Allows(options.Value.Login.AllowedEmails, email))
        {
            return Error.Forbidden("external.email_not_allowed", T("This email address may not sign in."));
        }

        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: false })
        {
            // someone may have registered the address without owning it; the owner confirms it before the accounts are joined
            return Error.Forbidden("external.local_unconfirmed", T("Confirm the email address of your account first, then sign in here again."));
        }

        if (user is null)
        {
            var created = await CreateAsync(login, email, cancellationToken);
            if (!created.IsSuccess)
            {
                return created;
            }

            user = created.Value;
        }

        if (await users.AddLoginAsync(user, new UserLoginInfo(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName)) is { Succeeded: false } added)
        {
            return Error.Validation("Login", string.Join(" ", added.Errors.Select(e => e.Description)));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Allowed(user);
    }

    private bool Verified(ExternalLoginInfo login) =>
        string.Equals(login.Principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)
        || (options.Value.External.Providers.TryGetValue(login.LoginProvider, out var provider) && provider.TrustEmail);

    private async Task<Result<User>> CreateAsync(ExternalLoginInfo login, string email, CancellationToken cancellationToken)
    {
        var policy = await RegistrationPolicy.LoadAsync(settings, cancellationToken);
        if (!options.Value.External.AutoProvision || !policy.Enabled || !Wildcards.Allows(registrationOptions.Value.AllowedEmails, email)
            || await tenants.GetSystemTenantIdAsync(cancellationToken) is not { } tenantId)
        {
            return Error.Forbidden("external.unknown_user", T("There is no account for {0}.", email));
        }

        var user = new User
        {
            TenantId = tenantId,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = login.Principal.FindFirstValue(ClaimTypes.GivenName),
            LastName = login.Principal.FindFirstValue(ClaimTypes.Surname),
        };
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user.Id, tenantId));
        if (await registration.CreateAsync(user, null, policy) is { Succeeded: false } created)
        {
            return Error.Validation("User", string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        await registration.AnnounceAsync(user, cancellationToken);
        return user;
    }

    private static Result<User> Allowed(User user) =>
        user.IsActive ? user : Error.Forbidden("external.inactive", T("This account is not active yet or was deactivated."));
}
