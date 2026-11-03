using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

internal static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("identity.user_not_found", "User not found.");
}
