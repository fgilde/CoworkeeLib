using Coworkee.Contracts.Configuration;
using Coworkee.Identity.Domain;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Account;

/// <summary>The password rules of Identity's own validator, read from the settings so administrators change them at runtime.</summary>
internal sealed class SettingsPasswordValidator(ISettingProvider settings) : IPasswordValidator<User>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        var describer = manager.ErrorDescriber;
        var value = password ?? string.Empty;
        var errors = new List<IdentityError>();

        var length = Math.Max(1, await settings.GetAsync<int>(PasswordSettings.RequiredLength));
        if (value.Length < length)
        {
            errors.Add(describer.PasswordTooShort(length));
        }

        if (await settings.GetAsync<bool>(PasswordSettings.RequireNonAlphanumeric) && value.All(c => IsDigit(c) || IsLower(c) || IsUpper(c)))
        {
            errors.Add(describer.PasswordRequiresNonAlphanumeric());
        }

        if (await settings.GetAsync<bool>(PasswordSettings.RequireDigit) && !value.Any(IsDigit))
        {
            errors.Add(describer.PasswordRequiresDigit());
        }

        if (await settings.GetAsync<bool>(PasswordSettings.RequireLowercase) && !value.Any(IsLower))
        {
            errors.Add(describer.PasswordRequiresLower());
        }

        if (await settings.GetAsync<bool>(PasswordSettings.RequireUppercase) && !value.Any(IsUpper))
        {
            errors.Add(describer.PasswordRequiresUpper());
        }

        var unique = await settings.GetAsync<int>(PasswordSettings.RequiredUniqueChars);
        if (unique > 0 && value.Distinct().Count() < unique)
        {
            errors.Add(describer.PasswordRequiresUniqueChars(unique));
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsLower(char c) => c is >= 'a' and <= 'z';

    private static bool IsUpper(char c) => c is >= 'A' and <= 'Z';
}
