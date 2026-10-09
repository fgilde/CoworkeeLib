using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Account.Email;

/// <summary>Changes the address and, when the user name was the address, the user name with it.</summary>
public static class EmailChange
{
    public static async Task<Error?> ApplyAsync(UserManager<User> users, User user, Func<Task<IdentityResult>> change)
    {
        var followsEmail = string.Equals(user.UserName, user.Email, StringComparison.OrdinalIgnoreCase);
        var changed = await change();
        if (!changed.Succeeded)
        {
            return IdentityErrors.ToError(changed);
        }

        return followsEmail && await users.SetUserNameAsync(user, user.Email) is { Succeeded: false } renamed ? IdentityErrors.ToError(renamed) : null;
    }
}
