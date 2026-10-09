using Coworkee.Contracts.Settings;
using Coworkee.Identity.Domain;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Account;

public static class SecuritySettings
{
    public const string MaxFailedAttempts = "Account.Lockout.MaxFailedAttempts";

    public const string LockoutMinutes = "Account.Lockout.Minutes";

    public const string PasswordExpiryDays = "Account.Password.ExpiryDays";

    public const string PasswordHistory = "Account.Password.History";
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

internal sealed class SecuritySettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Security", "Sign-in security")
            .Add(SecuritySettings.MaxFailedAttempts, "Failed sign-ins before lockout", SettingType.Int, [SettingScope.Global], "10",
                description: "Wrong passwords or codes in a row that lock the account for a while.")
            .Add(SecuritySettings.LockoutMinutes, "Lockout minutes", SettingType.Int, [SettingScope.Global], "15",
                description: "How long an account stays locked after too many failed sign-ins.")
            .Add(SecuritySettings.PasswordExpiryDays, "Password expiry in days", SettingType.Int, [SettingScope.Global], "0",
                description: "After so many days the sign-in asks for a new password; 0 never.")
            .Add(SecuritySettings.PasswordHistory, "Password history", SettingType.Int, [SettingScope.Global], "0",
                description: "A new password may not be one of the last so many; 0 allows any.");
}
