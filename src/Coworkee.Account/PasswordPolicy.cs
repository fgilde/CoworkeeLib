using Coworkee.Identity.Domain;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Account;

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
