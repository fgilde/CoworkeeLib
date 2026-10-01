using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

[DependsOn(typeof(CoworkeeIdentityModule))]
public sealed class CoworkeeAuthServerModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var options = context.Configuration.GetSection(AuthServerOptions.Section).Get<AuthServerOptions>() ?? new AuthServerOptions();
        services.Configure<AuthServerOptions>(context.Configuration.GetSection(AuthServerOptions.Section));
        services.AddSingleton<IModelContributor, AuthModelContributor>();
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
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<CoworkeeDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/endsession");
                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                server.RequireProofKeyForCodeExchange();
                server.RegisterScopes([Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess, .. options.ApiScopes.Keys]);
                server.SetAccessTokenLifetime(options.AccessTokenLifetime);
                // ponytail: development certificates; load signing/encryption certificates from configuration before production
                server.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
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

    public void ConfigureApplication(WebApplication app)
    {
        AuthEndpoints.Map(app);
        app.MapRazorPages();
    }
}
