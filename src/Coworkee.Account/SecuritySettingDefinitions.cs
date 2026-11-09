using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Coworkee.Account;

/// <summary>Defaults of the password rules come from the configured identity options, so a deployment's configuration stays the starting point.</summary>
internal sealed class SecuritySettingDefinitions(IOptions<IdentityOptions> identity) : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context)
    {
        var password = identity.Value.Password;
        context.Group("Security", "Sign-in security")
            .Add(PasswordSettings.RequiredLength, "Minimum password length", SettingType.Int, [SettingScope.Global], password.RequiredLength.ToString(),
                visibleToClient: true, description: "Shortest password allowed; at least 1.")
            .Add(PasswordSettings.RequiredUniqueChars, "Different characters in a password", SettingType.Int, [SettingScope.Global], password.RequiredUniqueChars.ToString(),
                visibleToClient: true, description: "A password needs at least so many different characters.")
            .Add(PasswordSettings.RequireDigit, "Password needs a digit", SettingType.Bool, [SettingScope.Global], Flag(password.RequireDigit), visibleToClient: true)
            .Add(PasswordSettings.RequireLowercase, "Password needs a lowercase letter", SettingType.Bool, [SettingScope.Global], Flag(password.RequireLowercase), visibleToClient: true)
            .Add(PasswordSettings.RequireUppercase, "Password needs an uppercase letter", SettingType.Bool, [SettingScope.Global], Flag(password.RequireUppercase), visibleToClient: true)
            .Add(PasswordSettings.RequireNonAlphanumeric, "Password needs a special character", SettingType.Bool, [SettingScope.Global], Flag(password.RequireNonAlphanumeric), visibleToClient: true)
            .Add(SecuritySettings.MaxFailedAttempts, "Failed sign-ins before lockout", SettingType.Int, [SettingScope.Global], "10",
                description: "Wrong passwords or codes in a row that lock the account for a while.")
            .Add(SecuritySettings.LockoutMinutes, "Lockout minutes", SettingType.Int, [SettingScope.Global], "15",
                description: "How long an account stays locked after too many failed sign-ins.")
            .Add(SecuritySettings.PasswordExpiryDays, "Password expiry in days", SettingType.Int, [SettingScope.Global], "0",
                description: "After so many days the sign-in asks for a new password; 0 never.")
            .Add(SecuritySettings.PasswordHistory, "Password history", SettingType.Int, [SettingScope.Global], "0",
                description: "A new password may not be one of the last so many; 0 allows any.")
            .Add(AuthSettings.PasskeysEnabled, "Passkeys", SettingType.Bool, [SettingScope.Global], "true", visibleToClient: true,
                description: "Users add passkeys to their account and sign in with them. Off hides them; stored passkeys stay.")
            .Add(AuthSettings.MaxSessionsPerUser, "Sessions per user", SettingType.Int, [SettingScope.Global, SettingScope.Tenant], "0", visibleToClient: true,
                description: "How many sessions (browsers, devices) a user may have at once; 0 is unlimited. A user's own limit wins.")
            .Add(AuthSettings.SessionLimitMode, "Beyond the session limit", SettingType.Choice, [SettingScope.Global, SettingScope.Tenant], SessionLimitModes.EndOldest,
                choices: SessionLimitModes.All, description: "EndOldest: a new sign-in ends the oldest sessions. Refuse: a new sign-in is refused until a session ends.");
    }

    private static string Flag(bool value) => value ? "true" : "false";
}
