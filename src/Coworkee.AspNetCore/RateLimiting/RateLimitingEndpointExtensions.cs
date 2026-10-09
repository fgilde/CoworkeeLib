using Microsoft.AspNetCore.Builder;

namespace Coworkee.AspNetCore.RateLimiting;

public static class RateLimitingEndpointExtensions
{
    /// <summary>Limits the endpoints with a policy of <see cref="CoworkeeRateLimitOptions.Policies"/>; nothing happens while rate limiting is disabled.</summary>
    public static TBuilder RequireCoworkeeRateLimit<TBuilder>(this TBuilder builder, string policy)
        where TBuilder : IEndpointConventionBuilder => builder.RequireRateLimiting(policy);
}
