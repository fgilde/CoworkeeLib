using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Users;

public static class PasswordHistory
{
    /// <summary>How many earlier hashes a user keeps; the password history setting can refuse up to this many.</summary>
    public const int Max = 24;

    public static IEnumerable<string> Of(User user) => user.PasswordHistory?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];
}
