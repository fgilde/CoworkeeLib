using Coworkee.Core.Results;

namespace Coworkee.Account;

internal static class AccountErrors
{
    public static readonly Error UserNotFound = Error.NotFound("identity.user_not_found", "The user does not exist.");

    public static readonly Error UserInactive = Error.Conflict("account.user_inactive", "Inactive users or users without an email address cannot reset their password.");
}
