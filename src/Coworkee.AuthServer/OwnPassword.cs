using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.AuthServer;

/// <summary>
/// A new password of the user himself ends his other sessions but not this one: the browser's sign-in carries a session id that
/// goes into its tokens, and those keep working until their next refresh, which issues them for the new stamp.
/// </summary>
internal static class OwnPassword
{
    public const string SessionItem = "coworkee_sid";

    public static Task NameSession(CookieSigningInContext context)
    {
        context.Properties.Items.TryAdd(SessionItem, Guid.NewGuid().ToString("N"));
        return Task.CompletedTask;
    }

    public static async Task<IdentityResult> ChangeAsync(
        HttpContext context, UserManager<User> users, SignInManager<User> signIn, CoworkeeDbContext db, User user, string current, string next)
    {
        var previous = SessionStamp.Hash(user.SecurityStamp);
        var result = await users.ChangePasswordAsync(user, current, next);
        if (!result.Succeeded)
        {
            return result;
        }

        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        user.KeptSession = cookie.Properties?.Items.TryGetValue(SessionItem, out var sid) == true && sid is not null
            ? SessionStamp.Keep(sid, previous, user.SecurityStamp)
            : null;

        // the store leaves saving to the unit of work
        await db.SaveChangesAsync(context.RequestAborted);
        await signIn.RefreshSignInAsync(user);
        return result;
    }
}
