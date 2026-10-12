using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AspNetCore.Authentication;

public static class ApiKeyForwarding
{
    private const string Selector = "Coworkee";

    /// <summary>
    /// Requests carrying an API key (<c>Authorization: Bearer cwk_…</c> or <c>X-Api-Key</c>) sign in through the
    /// <see cref="ApiKeyDefaults.Scheme"/> handler of the identity module; all others keep the default scheme configured before.
    /// </summary>
    public static IServiceCollection AddCoworkeeApiKeys(this IServiceCollection services)
    {
        string? fallback = null;
        services.AddAuthentication().AddPolicyScheme(Selector, null, policy =>
            policy.ForwardDefaultSelector = context => HasApiKey(context.Request) ? ApiKeyDefaults.Scheme : fallback);
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            if (options.DefaultAuthenticateScheme == Selector)
            {
                return;
            }

            fallback = options.DefaultAuthenticateScheme ?? options.DefaultScheme;
            options.DefaultAuthenticateScheme = Selector;
        });
        return services;
    }

    public static bool HasApiKey(HttpRequest request) =>
        request.Headers[ApiKeyDefaults.Header].ToString().Trim().Length > 0
        || request.Headers.Authorization.ToString().StartsWith("Bearer " + ApiKeyDefaults.Prefix, StringComparison.Ordinal);
}
