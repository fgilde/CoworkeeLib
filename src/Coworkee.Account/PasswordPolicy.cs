using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Coworkee.Identity.Domain;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Coworkee.Account;

public static class SecuritySettings
{
    public const string MaxFailedAttempts = "Account.Lockout.MaxFailedAttempts";

    public const string LockoutMinutes = "Account.Lockout.Minutes";

    public const string PasswordExpiryDays = "Account.Password.ExpiryDays";

    public const string PasswordHistory = "Account.Password.History";

    public const string PasswordRequiredLength = PasswordSettings.RequiredLength;

    public const string PasswordRequiredUniqueChars = PasswordSettings.RequiredUniqueChars;

    public const string PasswordRequireDigit = PasswordSettings.RequireDigit;

    public const string PasswordRequireLowercase = PasswordSettings.RequireLowercase;

    public const string PasswordRequireUppercase = PasswordSettings.RequireUppercase;

    public const string PasswordRequireNonAlphanumeric = PasswordSettings.RequireNonAlphanumeric;
}

/// <summary>Lockout and password rules from the settings, so administrators change them at runtime.</summary>
public sealed class PasswordPolicy(ISettingProvider settings, TimeProvider clock)
{
    /// <summary>Gives this request's user manager the lockout of the settings; the configured options stay untouched for other requests.</summary>
    public async Task ApplyLockoutAsync(UserManager<User> users, CancellationToken cancellationToken)
    {
        var options = users.Options;
        users.Options = new IdentityOptions
        {
            ClaimsIdentity = options.ClaimsIdentity,
            User = options.User,
            Password = options.Password,
            SignIn = options.SignIn,
            Tokens = options.Tokens,
            Stores = options.Stores,
            Lockout = new LockoutOptions
            {
                AllowedForNewUsers = options.Lockout.AllowedForNewUsers,
                MaxFailedAccessAttempts = Math.Max(1, await settings.GetAsync<int>(SecuritySettings.MaxFailedAttempts, cancellationToken)),
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Math.Max(1, await settings.GetAsync<int>(SecuritySettings.LockoutMinutes, cancellationToken))),
            },
        };
    }

    /// <summary>An administrator asked for a new password or the password expired; users without a password (external sign-in only) never have to.</summary>
    public async Task<bool> RequiresChangeAsync(User user, CancellationToken cancellationToken)
    {
        if (user.PasswordHash is null)
        {
            return false;
        }

        if (user.MustChangePassword)
        {
            return true;
        }

        var days = await settings.GetAsync<int>(SecuritySettings.PasswordExpiryDays, cancellationToken);
        return days > 0 && (user.PasswordChangedAt ?? user.CreatedAt).AddDays(days) <= clock.GetUtcNow();
    }
}

internal sealed class PasswordHistoryValidator(ISettingProvider settings) : IPasswordValidator<User>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        var count = Math.Min(await settings.GetAsync<int>(SecuritySettings.PasswordHistory), Identity.Users.PasswordHistory.Max + 1);
        var earlier = new[] { user.PasswordHash }.OfType<string>().Concat(Identity.Users.PasswordHistory.Of(user)).Take(count);
        return password is not null && earlier.Any(hash => manager.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed)
            ? IdentityResult.Failed((manager.ErrorDescriber as Identity.Users.LocalizedIdentityErrorDescriber)?.PasswordReused()
                ?? new IdentityError { Code = "PasswordReused", Description = "Choose a password you did not use recently." })
            : IdentityResult.Success;
    }
}

/// <summary>Defaults of the password rules come from the configured identity options, so a deployment's configuration stays the starting point.</summary>
internal sealed class SecuritySettingDefinitions(IOptions<IdentityOptions> identity) : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context)
    {
        var password = identity.Value.Password;
        context.Group("Security", "Sign-in security")
            .Add(SecuritySettings.PasswordRequiredLength, "Minimum password length", SettingType.Int, [SettingScope.Global], password.RequiredLength.ToString(),
                visibleToClient: true, description: "Shortest password allowed; at least 1.")
            .Add(SecuritySettings.PasswordRequiredUniqueChars, "Different characters in a password", SettingType.Int, [SettingScope.Global], password.RequiredUniqueChars.ToString(),
                visibleToClient: true, description: "A password needs at least so many different characters.")
            .Add(SecuritySettings.PasswordRequireDigit, "Password needs a digit", SettingType.Bool, [SettingScope.Global], Flag(password.RequireDigit), visibleToClient: true)
            .Add(SecuritySettings.PasswordRequireLowercase, "Password needs a lowercase letter", SettingType.Bool, [SettingScope.Global], Flag(password.RequireLowercase), visibleToClient: true)
            .Add(SecuritySettings.PasswordRequireUppercase, "Password needs an uppercase letter", SettingType.Bool, [SettingScope.Global], Flag(password.RequireUppercase), visibleToClient: true)
            .Add(SecuritySettings.PasswordRequireNonAlphanumeric, "Password needs a special character", SettingType.Bool, [SettingScope.Global], Flag(password.RequireNonAlphanumeric), visibleToClient: true)
            .Add(SecuritySettings.MaxFailedAttempts, "Failed sign-ins before lockout", SettingType.Int, [SettingScope.Global], "10",
                description: "Wrong passwords or codes in a row that lock the account for a while.")
            .Add(SecuritySettings.LockoutMinutes, "Lockout minutes", SettingType.Int, [SettingScope.Global], "15",
                description: "How long an account stays locked after too many failed sign-ins.")
            .Add(SecuritySettings.PasswordExpiryDays, "Password expiry in days", SettingType.Int, [SettingScope.Global], "0",
                description: "After so many days the sign-in asks for a new password; 0 never.")
            .Add(SecuritySettings.PasswordHistory, "Password history", SettingType.Int, [SettingScope.Global], "0",
                description: "A new password may not be one of the last so many; 0 allows any.");
    }

    private static string Flag(bool value) => value ? "true" : "false";
}
