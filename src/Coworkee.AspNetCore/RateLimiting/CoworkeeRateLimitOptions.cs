namespace Coworkee.AspNetCore.RateLimiting;

/// <summary>Fixed window limits per signed-in user, or per IP address for anonymous callers. Section <see cref="Section"/>.</summary>
public sealed class CoworkeeRateLimitOptions
{
    public const string Section = "Coworkee:RateLimiting";

    /// <summary>The sign-in, registration and password pages of the auth server.</summary>
    public const string Auth = "auth";

    /// <summary>Assistant chat, tools and MCP.</summary>
    public const string Ai = "ai";

    /// <summary>Not applied by the built-in modules; for application endpoints that take files.</summary>
    public const string Upload = "upload";

    public bool Enabled { get; set; } = true;

    /// <summary>A limit for every request; off unless configured.</summary>
    public RateLimitRule? Global { get; set; }

    /// <summary>Named policies for <see cref="RateLimitingEndpointExtensions.RequireCoworkeeRateLimit{TBuilder}"/>; configuration adds policies or changes these.</summary>
    public Dictionary<string, RateLimitRule> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [Auth] = new() { PermitLimit = 60 },
        [Ai] = new() { PermitLimit = 30 },
        [Upload] = new() { PermitLimit = 120 },
    };
}

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; } = 100;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
