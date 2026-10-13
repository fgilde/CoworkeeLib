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

        var rules = new PasswordRules(
            await settings.GetAsync<int>(PasswordSettings.RequiredLength),
            await settings.GetAsync<int>(PasswordSettings.RequiredUniqueChars),
            await settings.GetAsync<bool>(PasswordSettings.RequireDigit),
            await settings.GetAsync<bool>(PasswordSettings.RequireLowercase),
            await settings.GetAsync<bool>(PasswordSettings.RequireUppercase),
            await settings.GetAsync<bool>(PasswordSettings.RequireNonAlphanumeric)).Normalized();

        if (string.IsNullOrWhiteSpace(value) || value.Length < rules.RequiredLength)
        {
            errors.Add(describer.PasswordTooShort(rules.RequiredLength));
        }

        if (rules.RequireNonAlphanumeric && value.All(c => IsDigit(c) || IsLower(c) || IsUpper(c)))
        {
            errors.Add(describer.PasswordRequiresNonAlphanumeric());
        }

        if (rules.RequireDigit && !value.Any(IsDigit))
        {
            errors.Add(describer.PasswordRequiresDigit());
        }

        if (rules.RequireLowercase && !value.Any(IsLower))
        {
            errors.Add(describer.PasswordRequiresLower());
        }

        if (rules.RequireUppercase && !value.Any(IsUpper))
        {
            errors.Add(describer.PasswordRequiresUpper());
        }

        if (rules.RequiredUniqueChars > 0 && value.Distinct().Count() < rules.RequiredUniqueChars)
        {
            errors.Add(describer.PasswordRequiresUniqueChars(rules.RequiredUniqueChars));
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsLower(char c) => c is >= 'a' and <= 'z';

    private static bool IsUpper(char c) => c is >= 'A' and <= 'Z';
}
