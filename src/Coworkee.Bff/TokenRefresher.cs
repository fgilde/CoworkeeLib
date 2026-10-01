using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Coworkee.Bff;

internal sealed class TokenRefresher(IOptionsMonitor<OpenIdConnectOptions> oidc, IHttpClientFactory httpClients, TimeProvider clock) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var expiresAt = context.Properties.GetTokenValue("expires_at");
        if (expiresAt is null || DateTimeOffset.Parse(expiresAt, CultureInfo.InvariantCulture) > clock.GetUtcNow().AddSeconds(60))
        {
            return;
        }

        var refreshToken = context.Properties.GetTokenValue("refresh_token");
        var options = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = options.ConfigurationManager is null ? options.Configuration : await options.ConfigurationManager.GetConfigurationAsync(context.HttpContext.RequestAborted);
        if (refreshToken is null || configuration?.TokenEndpoint is null)
        {
            context.RejectPrincipal();
            return;
        }

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = options.ClientId!,
        };
        if (!string.IsNullOrEmpty(options.ClientSecret))
        {
            form["client_secret"] = options.ClientSecret;
        }

        using var response = await httpClients.CreateClient(nameof(TokenRefresher)).PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(form), context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        var root = json.RootElement;
        context.Properties.UpdateTokenValue("access_token", root.GetProperty("access_token").GetString()!);
        if (root.TryGetProperty("refresh_token", out var newRefresh))
        {
            context.Properties.UpdateTokenValue("refresh_token", newRefresh.GetString()!);
        }

        var expires = clock.GetUtcNow().AddSeconds(root.GetProperty("expires_in").GetInt32());
        context.Properties.UpdateTokenValue("expires_at", expires.ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }
}
