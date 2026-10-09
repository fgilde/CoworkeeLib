using System.Security.Cryptography.X509Certificates;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.AuthServer.External;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Coworkee.Identity.Domain;
using Coworkee.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeAuthStoreModule), typeof(Coworkee.Account.CoworkeeAccountModule), typeof(Coworkee.Theming.CoworkeeThemingModule))]
public sealed class CoworkeeAuthServerModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var options = context.Configuration.GetSection(AuthServerOptions.Section).Get<AuthServerOptions>() ?? new AuthServerOptions();
        services.Configure<AuthServerOptions>(context.Configuration.GetSection(AuthServerOptions.Section));
        services.Configure<RegistrationOptions>(context.Configuration.GetSection(RegistrationOptions.Section));
        services.Configure<IdentityOptions>(identity => identity.SignIn.RequireConfirmedEmail = true);
        services.AddScoped<Registration.AccountRegistration>();
        services.AddScoped<AuthBrandingProvider>();
        services.AddSingleton<Application.Localization.ITextTranslator, AuthTextTranslator>();
        services.AddSingleton<AuthClientSeeder>();
        services.AddHostedService(provider => provider.GetRequiredService<AuthClientSeeder>());
        services.AddRazorPages().AddApplicationPart(typeof(CoworkeeAuthServerModule).Assembly);

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        authentication.AddExternalProviders(options.External);
        services.AddScoped<ExternalSignIn>();
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.LoginPath = "/Account/Login";
            cookie.LogoutPath = "/Account/Logout";
            var signingIn = cookie.Events.OnSigningIn;
            cookie.Events.OnSigningIn = async signIn =>
            {
                await signingIn(signIn);
                await OwnPassword.NameSession(signIn);
            };
        });
        new IdentityBuilder(typeof(User), typeof(Role), services).AddSignInManager();

        services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/endsession");
                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().AllowClientCredentialsFlow();
                server.RequireProofKeyForCodeExchange();
                server.RegisterScopes([Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess, .. options.ApiScopes.Keys]);
                server.SetAccessTokenLifetime(options.AccessTokenLifetime);
                AddCertificates(server, options, context.Configuration);
                server.DisableAccessTokenEncryption();
                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
                if (options.AllowHttp)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();
            });
    }

    /// <summary>
    /// Configured certificates; the per machine development certificates only in Development or when allowed explicitly,
    /// because tokens signed with them break on every other instance and after the machine changes.
    /// </summary>
    private static void AddCertificates(OpenIddictServerBuilder server, AuthServerOptions options, IConfiguration configuration)
    {
        if (options.SigningCertificate is { } signing && options.EncryptionCertificate is { } encryption)
        {
            server.AddSigningCertificate(Load(signing)).AddEncryptionCertificate(Load(encryption));
        }
        else if (options.DevelopmentCertificates
            || string.Equals(configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase))
        {
            server.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        }
        else
        {
            throw new InvalidOperationException(
                $"Configure {AuthServerOptions.Section}:SigningCertificate and :EncryptionCertificate (Path, Password) for the auth server, or set {AuthServerOptions.Section}:DevelopmentCertificates for tests and demos.");
        }

        if (options.Issuer is { } issuer)
        {
            server.SetIssuer(issuer);
        }
    }

    /// <summary>
    /// The account pages run no script and style inline; after sign-in the browser follows redirects to the clients, which
    /// browsers check against form-action, so the clients' origins are allowed there. A logo from elsewhere adds its origin to img-src.
    /// </summary>
    internal static string ContentSecurityPolicy(AuthServerOptions options, IEnumerable<string> storedClientUris)
    {
        var clients = options.Clients.SelectMany(c => c.RedirectUris.Concat(c.PostLogoutRedirectUris)).Concat(storedClientUris)
            .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null)
            .OfType<string>().Concat(ExternalProviders.Origins(options.External)).Distinct(StringComparer.OrdinalIgnoreCase);
        var logo = Uri.TryCreate(options.LogoUrl, UriKind.Absolute, out var logoUri) && logoUri.Scheme is "https" or "http" ? " " + logoUri.GetLeftPart(UriPartial.Authority) : string.Empty;
        return $"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:{logo}; object-src 'none'; base-uri 'self'; "
            + $"frame-ancestors 'none'; form-action {string.Join(' ', ["'self'", .. clients])}";
    }

    // clients added in the admin pages redirect to their origins as well; the auth server learns about them within a minute
    private static async Task<string[]> StoredClientUrisAsync(Microsoft.AspNetCore.Http.HttpContext context)
    {
        try
        {
            return await ClientUrisAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // before the database is migrated
            return [];
        }
    }

    private static async Task<string[]> ClientUrisAsync(Microsoft.AspNetCore.Http.HttpContext context) =>
        await context.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>().GetOrCreateAsync(
            "coworkee:auth-client-uris",
            async ct =>
            {
                var applications = context.RequestServices.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();
                var uris = new List<string>();
                await foreach (var application in applications.ListAsync(null, null, ct))
                {
                    uris.AddRange(await applications.GetRedirectUrisAsync(application, ct));
                    uris.AddRange(await applications.GetPostLogoutRedirectUrisAsync(application, ct));
                }

                return uris.ToArray();
            },
            new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1) },
            cancellationToken: context.RequestAborted);

    private static X509Certificate2 Load(CertificateOptions certificate) =>
        X509CertificateLoader.LoadPkcs12FromFile(certificate.Path, certificate.Password, X509KeyStorageFlags.EphemeralKeySet);

    public void ConfigureApplication(WebApplication app)
    {
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthServerOptions>>().Value;
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy(options, await StoredClientUrisAsync(context));
            if (context.Request.Path.StartsWithSegments("/Account"))
            {
                await context.RequestServices.GetRequiredService<Coworkee.Account.PasswordPolicy>()
                    .ApplyLockoutAsync(context.RequestServices.GetRequiredService<UserManager<User>>(), context.RequestAborted);
            }

            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });

        AuthEndpoints.Map(app);
        app.MapRazorPages().RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Auth);
    }
}
