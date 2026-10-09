using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Users;

/// <summary>
/// Access tokens carry a hash of the user's security stamp; signing a user out everywhere, locking or a new password changes the
/// stamp, and the API refuses the tokens issued before right away instead of when they expire.
/// </summary>
public static class SessionStamp
{
    public const string ClaimType = "stamp";

    public static string Hash(string? securityStamp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty)))[..16];

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
                    && !await IsCurrentAsync(context.HttpContext.RequestServices, userId, stamp, context.HttpContext.RequestAborted))
                {
                    context.Fail("The session was ended.");
                }
            };
        });

    // cached with the permissions: every change of a user drops the entry
    // ponytail: the drop reaches only this instance; other API instances notice within the cache lifetime, a distributed L2 cache closes that gap
    private static async Task<bool> IsCurrentAsync(IServiceProvider services, Guid userId, string stamp, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<CoworkeeDbContext>();
        var current = await services.GetRequiredService<HybridCache>().GetOrCreateAsync(
            $"coworkee:stamp:{userId}",
            async ct => await db.Set<User>().Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleOrDefaultAsync(ct) ?? string.Empty,
            tags: [PermissionCache.Tag],
            cancellationToken: cancellationToken);
        return Hash(current) == stamp;
    }
}
