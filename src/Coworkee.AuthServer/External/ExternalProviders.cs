using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.External;

internal static class ExternalProviders
{
    public static AuthenticationBuilder AddExternalProviders(this AuthenticationBuilder authentication, ExternalLoginOptions options)
    {
        foreach (var (scheme, provider) in options.Providers)
        {
            authentication.AddOpenIdConnect(scheme, provider.DisplayName ?? scheme, oidc => Configure(oidc, scheme, provider));
        }

        return authentication;
    }

    /// <summary>The origins sign-in forms may redirect to (content security policy form-action).</summary>
    public static IEnumerable<string> Origins(ExternalLoginOptions options) =>
        options.Providers.Values
            .Select(p => Uri.TryCreate(p.Authority, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null)
            .OfType<string>();

    private static void Configure(OpenIdConnectOptions oidc, string scheme, ExternalProviderOptions provider)
    {
        oidc.SignInScheme = IdentityConstants.ExternalScheme;
        oidc.Authority = provider.Authority;
        oidc.ClientId = provider.ClientId;
        oidc.ClientSecret = provider.ClientSecret;
        oidc.RequireHttpsMetadata = provider.RequireHttpsMetadata;
        oidc.ResponseType = "code";
        oidc.UsePkce = true;
        oidc.CallbackPath = $"/signin-{scheme}";
        oidc.SignedOutCallbackPath = $"/signout-callback-{scheme}";
        oidc.GetClaimsFromUserInfoEndpoint = true;
        oidc.SaveTokens = false;
        oidc.Scope.Clear();
        foreach (var scope in provider.Scopes)
        {
            oidc.Scope.Add(scope);
        }
    }
}
