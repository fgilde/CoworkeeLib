using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AspNetCore.Http;

/// <summary>
/// Cross-origin calls from the origins of <see cref="CoworkeeCorsOptions"/>. <see cref="Policy"/> applies to every request (<c>UseCoworkee</c>) and allows
/// no credentials: a single-page app sends its token in the Authorization header. Endpoints a browser calls with credentials (SignalR's negotiate)
/// opt into <see cref="WithCredentials"/> with <c>.RequireCors(CoworkeeCors.WithCredentials)</c>.
/// </summary>
public static class CoworkeeCors
{
    public const string Policy = "coworkee";

    public const string WithCredentials = "coworkee-credentials";

    internal static void AddCoworkeeCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = Origins(configuration.GetSection(CoworkeeCorsOptions.Section).Get<CoworkeeCorsOptions>()?.Origins ?? []);
        services.AddCors(cors =>
        {
            cors.AddPolicy(Policy, policy => Allow(policy, origins));
            cors.AddPolicy(WithCredentials, policy => Allow(policy, origins).AllowCredentials());
        });
    }

    // downloads name their file in Content-Disposition, which a cross-origin script reads only when exposed
    private static CorsPolicyBuilder Allow(CorsPolicyBuilder policy, string[] origins) =>
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Content-Disposition").SetPreflightMaxAge(TimeSpan.FromMinutes(10));

    internal static string[] Origins(IEnumerable<string> configured) =>
        [.. configured.Where(o => !string.IsNullOrWhiteSpace(o)).Select(Origin).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string Origin(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0
            ? uri.GetLeftPart(UriPartial.Authority)
            : throw new InvalidOperationException($"{CoworkeeCorsOptions.Section}:Origins: '{value}' is no http(s) origin. Name every origin exactly, '*' is not allowed.");
}
