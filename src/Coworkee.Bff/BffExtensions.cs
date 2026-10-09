using System.Net.Http.Headers;
using System.Security.Claims;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

namespace Coworkee.Bff;

public static class BffExtensions
{
    private const string LoginHintItem = "login_hint";

    private const string CsrfHeader = "X-CSRF";

    private const string SwaggerUi = "/swagger";

    public static WebApplicationBuilder AddCoworkeeBff(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(BffOptions.Section).Get<BffOptions>() ?? new BffOptions();
        builder.Services.Configure<BffOptions>(builder.Configuration.GetSection(BffOptions.Section));
        builder.Services.AddHttpClient(nameof(TokenRefresher));
        builder.Services.AddScoped<TokenRefresher>();
        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpForwarderWithServiceDiscovery();

        builder.Services.AddAuthentication(auth =>
            {
                auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.EventsType = typeof(TokenRefresher);
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.ResponseType = OpenIdConnectResponseType.Code;

                // the code comes back as a plain redirect: a form_post answer needs an inline auto-submit script that the
                // auth server's content security policy refuses; PKCE protects the code
                oidc.ResponseMode = OpenIdConnectResponseMode.Query;
                oidc.UsePkce = true;
                oidc.Events.OnRedirectToIdentityProvider = context =>
                {
                    // e.g. right after setup: the sign-in form starts with the administrator's address
                    if (context.Properties.Items.TryGetValue(LoginHintItem, out var hint) && !string.IsNullOrWhiteSpace(hint))
                    {
                        context.ProtocolMessage.LoginHint = hint;
                    }

                    return Task.CompletedTask;
                };
                oidc.SaveTokens = true;
                oidc.MapInboundClaims = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.RequireHttpsMetadata = !options.Authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
                oidc.TokenValidationParameters.NameClaimType = "name";
                oidc.TokenValidationParameters.RoleClaimType = "role";
                oidc.Scope.Clear();
                foreach (var scope in new[] { "openid", "profile", "email", "roles", "offline_access" }.Concat(options.Scopes))
                {
                    oidc.Scope.Add(scope);
                }

                if (options.AuthorizationEndpoint is not null)
                {
                    oidc.Configuration = new OpenIdConnectConfiguration
                    {
                        AuthorizationEndpoint = options.AuthorizationEndpoint,
                        TokenEndpoint = options.Authority.TrimEnd('/') + "/connect/token",
                        EndSessionEndpoint = options.Authority.TrimEnd('/') + "/connect/endsession",
                    };
                }
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, ClientPolicyProvider>();
        return builder;
    }

    private static bool IsLocalPath([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url) =>
        url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = "/" });
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var redirect = context.Response.Headers.Location.ToString();
        context.Response.Headers.Location = default;
        context.Response.StatusCode = StatusCodes.Status200OK;
        return Results.Ok(new BffLogoutDto(redirect.Length > 0 ? redirect : "/"));
    }

    public static WebApplication MapCoworkeeBff(this WebApplication app)
    {
        var options = app.Configuration.GetSection(BffOptions.Section).Get<BffOptions>() ?? new BffOptions();
        app.Use(async (context, next) =>
        {
            var unsafeMethod = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
            if (unsafeMethod
                && (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/bff/logout"))
                && context.Request.Headers[CsrfHeader] != "1")
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/bff/login", (string? returnUrl, string? loginHint) => Results.Challenge(
            new AuthenticationProperties(loginHint is { Length: > 0 and <= 256 } ? new Dictionary<string, string?> { [LoginHintItem] = loginHint } : [])
            {
                RedirectUri = IsLocalPath(returnUrl) ? returnUrl : "/",
            },
            [OpenIdConnectDefaults.AuthenticationScheme]));
        app.MapPost("/bff/logout", (Delegate)LogoutAsync);
        app.MapGet("/bff/user", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
            ? new BffUserDto(
                true,
                user.FindFirstValue("name"),
                user.FindFirstValue("email"),
                Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null,
                Guid.TryParse(user.FindFirstValue("tenant"), out var tenant) ? tenant : null,
                user.FindAll("role").Select(c => c.Value).ToArray(),
                options.Authority.TrimEnd('/') + "/Account/Manage")
            : BffUserDto.Anonymous);

        // the assistant may think and call tools for minutes before the first byte of its answer
        var longRequests = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(10) };
        foreach (var prefix in options.ForwardedPrefixes.Prepend("/openapi").Prepend(SwaggerUi).Prepend("/odata").Prepend("/api").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var forwarder = app.MapForwarder(prefix.TrimEnd('/') + "/{**catch-all}", options.ApiAddress, longRequests, transforms => transforms.AddRequestTransform(async transform =>
            {
                var token = await transform.HttpContext.GetTokenAsync("access_token");
                if (token is not null)
                {
                    transform.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }));

            // the API enforces its own body limits per endpoint, large file uploads stream through
            forwarder.WithMetadata(new Microsoft.AspNetCore.Mvc.DisableRequestSizeLimitAttribute());

            // signed-out visitors of the API docs go through the login first; the API checks the permission
            if (prefix == SwaggerUi)
            {
                forwarder.RequireAuthorization();
            }
        }

        return app;
    }
}
