using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Users;

/// <summary>
/// Access tokens carry a hash of the user's security stamp; signing a user out everywhere, locking or a new password changes the
/// stamp, and the API refuses the tokens issued before right away instead of when they expire. Tokens also name the sign-in
/// session (<see cref="SessionClaimType"/>): the session that changed its own password keeps its tokens (<see cref="Keep"/>).
/// </summary>
public static class SessionStamp
{
    public const string ClaimType = "stamp";

    public const string SessionClaimType = "sid";

    /// <summary>HttpContext item of the auth server: the session of the request's own sign-in, which a stamp change on its account pages keeps.</summary>
    public const string RequestSessionItem = "coworkee:request-sid";

    public static string Hash(string? securityStamp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty)))[..16];

    /// <summary>Lets the session <paramref name="sid"/> keep its tokens of <paramref name="previousHash"/> while the stamp stays <paramref name="securityStamp"/>.</summary>
    public static string Keep(string sid, string previousHash, string? securityStamp) => $"{sid} {previousHash} {Hash(securityStamp)}";

    public static bool IsCurrent(UserStamp user, string stamp, string? sid) =>
        !IsEnded(user, sid) && (Hash(user.SecurityStamp) == stamp || (sid is { Length: > 0 } && user.KeptSession == Keep(sid, stamp, user.SecurityStamp)));

    /// <summary>Whether the session <paramref name="sid"/> was ended on its own (<see cref="Sessions.UserSession"/>).</summary>
    public static bool IsEnded(UserStamp user, string? sid) =>
        sid is { Length: > 0 } && user.EndedSessions?.Split(' ').Contains(sid, StringComparer.Ordinal) == true;

    internal static void ValidateBearerTokens(this IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
        {
            var validated = jwt.Events.OnTokenValidated;
            jwt.Events.OnTokenValidated = async context =>
            {
                await validated(context);
                if (context.Result is null
                    && context.Principal?.FindFirstValue(ClaimType) is { } stamp
                    && Guid.TryParse(context.Principal.FindFirstValue("sub"), out var userId)
                    && !IsCurrent(await context.HttpContext.RequestServices.GetRequiredService<SessionStamps>().GetAsync(userId, context.HttpContext.RequestAborted),
                        stamp, context.Principal.FindFirstValue(SessionClaimType)))
                {
                    context.Fail("The session was ended.");
                }
            };
        });
}
