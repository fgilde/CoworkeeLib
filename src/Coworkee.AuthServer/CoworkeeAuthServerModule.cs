using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography.X509Certificates;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeAuthStoreModule), typeof(Coworkee.Account.CoworkeeAccountModule))]
public sealed class CoworkeeAuthServerModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var options = context.Configuration.GetSection(AuthServerOptions.Section).Get<AuthServerOptions>() ?? new AuthServerOptions();
        services.Configure<AuthServerOptions>(context.Configuration.GetSection(AuthServerOptions.Section));
        services.AddSingleton<AuthClientSeeder>();
        services.AddHostedService(provider => provider.GetRequiredService<AuthClientSeeder>());
        services.AddRazorPages().AddApplicationPart(typeof(CoworkeeAuthServerModule).Assembly);

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.LoginPath = "/Account/Login";
            cookie.LogoutPath = "/Account/Logout";
        });
        new IdentityBuilder(typeof(User), typeof(Role), services).AddSignInManager();

        services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/endsession");
                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
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

    private static X509Certificate2 Load(CertificateOptions certificate) =>
        X509CertificateLoader.LoadPkcs12FromFile(certificate.Path, certificate.Password, X509KeyStorageFlags.EphemeralKeySet);

    public void ConfigureApplication(WebApplication app)
    {
        AuthEndpoints.Map(app);
        app.MapRazorPages();
    }
}
