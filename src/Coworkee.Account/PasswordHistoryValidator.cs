using Coworkee.Identity.Domain;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Account;

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
