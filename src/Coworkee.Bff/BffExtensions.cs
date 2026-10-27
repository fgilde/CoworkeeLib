using System.Net.Http.Headers;
using System.Security.Claims;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

namespace Coworkee.Bff;

public static class BffExtensions
{
    private const string LoginHintItem = "login_hint";

    private const string CsrfHeader = "X-CSRF";

    private const string SwaggerUi = "/swagger";

    private const string SessionCache = "coworkee:bff:sessions";

    public static WebApplicationBuilder AddCoworkeeBff(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(BffOptions.Section).Get<BffOptions>() ?? new BffOptions();
        builder.Services.Configure<BffOptions>(builder.Configuration.GetSection(BffOptions.Section));
        if (options.FrameAncestors.Count > 0)
        {
            // antiforgery would forbid every frame; MapCoworkeeBff allows the listed origins instead
            builder.Services.Configure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(o => o.SuppressXFrameOptionsHeader = true);
        }

        builder.Services.AddHttpClient(nameof(TokenRefresher));
        builder.Services.AddScoped<TokenRefresher>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<LoadingScreenSource>();
        builder.Services.AddHttpClient(LoadingScreenSource.Client, client =>
            {
                client.BaseAddress = Uri.TryCreate(options.ApiAddress.TrimEnd('/') + "/", UriKind.Absolute, out var api) ? api : null;
                client.Timeout = LoadingScreenSource.Timeout;
            })
            .AddServiceDiscovery();
        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpForwarderWithServiceDiscovery();

        builder.Services.AddAuthentication(auth =>
            {
                auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                if (options.CookieName is { Length: > 0 } name)
                {
                    cookie.Cookie.Name = name;
                }

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
        AddSessionStore(builder);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, ClientPolicyProvider>();
        return builder;
    }

    // the tokens stay on the server: a cookie carrying them grows past 8 KB, and browsers send it to every localhost port (Keycloak answers 431)
    private static void AddSessionStore(WebApplicationBuilder builder)
    {
        var redis = builder.Configuration.GetConnectionString("redis");
        builder.Services.AddKeyedSingleton<IDistributedCache>(SessionCache, (_, _) => redis is { Length: > 0 }
            ? new RedisCache(Options.Create(new RedisCacheOptions { ConfigurationOptions = Reconnecting(redis) }))
            : new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IServiceProvider>((cookie, services) => cookie.SessionStore = new CacheTicketStore(services.GetRequiredKeyedService<IDistributedCache>(SessionCache)));
        if (redis is { Length: > 0 })
        {
            // every instance and every restart reads the session cookie with the same keys
            var connection = new Lazy<StackExchange.Redis.IConnectionMultiplexer>(() => StackExchange.Redis.ConnectionMultiplexer.Connect(Reconnecting(redis)));
            builder.Services.AddDataProtection().SetApplicationName("Coworkee.Bff")
                .PersistKeysToStackExchangeRedis(() => connection.Value.GetDatabase(), "coworkee:bff:data-protection");
        }
    }

    private static StackExchange.Redis.ConfigurationOptions Reconnecting(string redis)
    {
        var options = StackExchange.Redis.ConfigurationOptions.Parse(redis);
        options.AbortOnConnectFail = false;
        return options;
    }

    // Url.IsLocalUrl's rules for a path, stricter on backslashes: browsers read "/\host" and "/<tab>/host" as another site
    private static bool IsLocalPath([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url) =>
        url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || url[1] != '/') && !url.Any(c => c == '\\' || char.IsControl(c));

    // "Log in as" and back switch the sign-in of the auth server, which then sends the browser through /bff/login, so this session follows
    private static IResult ToAuthServer(BffOptions options, HttpContext context, string page, string? returnUrl, params KeyValuePair<string, string?>[] parameters)
    {
        if (returnUrl is not null && !IsLocalPath(returnUrl))
        {
            return Results.BadRequest();
        }

        var login = UriHelper.BuildAbsolute(context.Request.Scheme, context.Request.Host, context.Request.PathBase, "/bff/login", QueryString.Create("returnUrl", returnUrl ?? "/"));
        return Results.Redirect(options.Authority.TrimEnd('/') + page + QueryString.Create([.. parameters, new("returnUrl", login)]));
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = "/" });
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var redirect = context.Response.Headers.Location.ToString();
        context.Response.Headers.Location = default;
        context.Response.StatusCode = StatusCodes.Status200OK;
        return Results.Ok(new BffLogoutDto(redirect.Length > 0 ? redirect : "/"));
    }

    /// <summary>The policy that lets the configured origins frame the pages, null when there are none.</summary>
    internal static string? FrameAncestors(BffOptions options)
    {
        var origins = options.FrameAncestors.Select(o => Uri.TryCreate(o, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null).OfType<string>().Distinct().ToList();
        return origins.Count == 0 ? null : $"frame-ancestors 'self' {string.Join(' ', origins)}";
    }

    public static WebApplication MapCoworkeeBff(this WebApplication app)
    {
        var options = app.Configuration.GetSection(BffOptions.Section).Get<BffOptions>() ?? new BffOptions();
        // UseCoworkeeSecurityHeaders puts frame-ancestors into its policy; a second policy would only narrow it
        if (FrameAncestors(options) is { } ancestors && !((IApplicationBuilder)app).Properties.ContainsKey(SecurityHeaders.Marker))
        {
            app.Use((context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.Append("Content-Security-Policy", ancestors);
                    return Task.CompletedTask;
                });
                return next(context);
            });
        }

        app.Use(async (context, next) =>
        {
            var unsafeMethod = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
            if (unsafeMethod
                && (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/bff/logout"))
                && context.Request.Headers[CsrfHeader] != "1"
                && !options.CsrfExemptPaths.Any(p => context.Request.Path.Equals(p, StringComparison.OrdinalIgnoreCase)))
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
        app.MapGet("/bff/impersonate", (HttpContext context, string? ticket, string? returnUrl) => ticket is { Length: > 0 }
            ? ToAuthServer(options, context, "/Account/Impersonate", returnUrl, KeyValuePair.Create("ticket", (string?)ticket))
            : Results.BadRequest());
        app.MapGet("/bff/stop-impersonation", (HttpContext context, string? returnUrl) => ToAuthServer(options, context, "/Account/StopImpersonation", returnUrl));
        app.MapGet(LoadingScreenLogo.Path, LoadingScreenLogo.ServeAsync);
        app.MapGet(LoadingScreenScript.Path, LoadingScreenScript.Serve);
        Pwa.PwaEndpoints.Map(app);
        var launcher = app.Configuration["Coworkee:AirWorks:Launcher"] is { Length: > 0 } configured ? configured : null;
        app.MapGet("/bff/user", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
            ? new BffUserDto(
                true,
                user.FindFirstValue("name"),
                user.FindFirstValue("email"),
                Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null,
                Guid.TryParse(user.FindFirstValue("tenant"), out var tenant) ? tenant : null,
                user.FindAll("role").Select(c => c.Value).ToArray(),
                // the account pages refuse someone signed in as another user
                user.HasClaim(c => c.Type == ImpersonationClaims.Impersonator) ? null : options.Authority.TrimEnd('/') + "/Account/Manage",
                user.FindFirstValue("system_tenant") is { } system ? system == "true" : null,
                options.WebAppUrl is { Length: > 0 } web ? web.TrimEnd('/') : null,
                launcher,
                Guid.TryParse(user.FindFirstValue(ImpersonationClaims.Impersonator), out var impersonator) ? impersonator : null,
                user.FindFirstValue(ImpersonationClaims.ImpersonatorName))
            : BffUserDto.Anonymous with { Launcher = launcher });

        // the assistant may think and call tools for minutes before the first byte of its answer
        var longRequests = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(10) };
        foreach (var prefix in options.ForwardedPrefixes.Prepend("/openapi").Prepend(SwaggerUi).Prepend("/odata").Prepend("/api").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var forwarder = app.MapForwarder(prefix.TrimEnd('/') + "/{**catch-all}", options.ApiAddress, longRequests, transforms =>
            {
                transforms.AddRequestTransform(async transform =>
                {
                    var token = await transform.HttpContext.GetTokenAsync("access_token");
                    if (token is not null)
                    {
                        transform.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                });

                // the API refused a token the session still held: an administrator signed the user out or locked the account
                transforms.AddResponseTransform(async transform =>
                {
                    if (transform.ProxyResponse?.StatusCode == System.Net.HttpStatusCode.Unauthorized && transform.HttpContext.User.Identity?.IsAuthenticated == true)
                    {
                        await transform.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                });
            });

            // the API enforces its own body limits per endpoint, large file uploads stream through past Kestrel's 30 MB
            forwarder.WithMetadata(options.MaxRequestBodySize is { } max
                ? new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(max)
                : new Microsoft.AspNetCore.Mvc.DisableRequestSizeLimitAttribute());

            // signed-out visitors of the API docs go through the login first; the API checks the permission
            if (prefix == SwaggerUi)
            {
                forwarder.RequireAuthorization();
            }
        }

        McpForwarding.MapMcpForwarder(app, options.ApiAddress, longRequests);
        return app;
    }
}
