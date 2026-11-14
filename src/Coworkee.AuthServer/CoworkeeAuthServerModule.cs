using System.Security.Cryptography.X509Certificates;
using Coworkee.Application.Registration;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.AuthServer.External;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Server;
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
        services.AddOptions<RegistrationOptions>().Bind(context.Configuration.GetSection(RegistrationOptions.Section))
            .Validate<IServiceProviderIsService>(
                (registration, registered) => !registration.RequireDocuments || registration.Documents.Count == 0 || registered.IsService(typeof(IRegistrationDocumentStore)),
                $"{RegistrationOptions.Section}:Documents needs an {nameof(IRegistrationDocumentStore)}: add Coworkee.Files or register your own store.")
            .ValidateOnStart();
        services.Configure<IdentityOptions>(identity => identity.SignIn.RequireConfirmedEmail = true);
        // a passkey skips the second step, so the device has to verify its user (overridable, but the sign-in still refuses unverified ones)
        services.Configure<IdentityPasskeyOptions>(passkeys => passkeys.UserVerificationRequirement = "required");
        services.Configure<IdentityPasskeyOptions>(context.Configuration.GetSection(Passkeys.Section));
        services.AddScoped<Registration.AccountRegistration>();
        services.AddScoped<AuthBrandingProvider>();
        services.AddSingleton<Application.Localization.ITextTranslator, AuthTextTranslator>();
        services.AddSingleton<AuthClientSeeder>();
        services.AddHostedService(provider => provider.GetRequiredService<AuthClientSeeder>());
        services.AddHttpContextAccessor();
        services.AddRazorPages(pages =>
        {
            AuthImpersonation.Conventions(pages);
            // the preview of the Themes page is the sign-in page itself; links keep going to /Account/Login
            pages.Conventions.AddPageRouteModelConvention("/Account/Login", model => model.Selectors.Add(new Microsoft.AspNetCore.Mvc.ApplicationModels.SelectorModel
            {
                AttributeRouteModel = new Microsoft.AspNetCore.Mvc.ApplicationModels.AttributeRouteModel { Template = SignInPreview.Path.TrimStart('/'), SuppressLinkGeneration = true },
            }));
        }).AddApplicationPart(typeof(CoworkeeAuthServerModule).Assembly);
        if (options.FrameAncestors.Count > 0)
        {
            // antiforgery's X-Frame-Options would forbid every frame; frame-ancestors of the policy allows the listed origins instead
            services.Configure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(o => o.SuppressXFrameOptionsHeader = true);
        }

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
                await AuthSessions.StartAsync(signIn);
            };
            // the sign-in on the login page sends the browser on by itself; a refused one stays on the page with its message
            var redirecting = cookie.Events.OnRedirectToReturnUrl;
            cookie.Events.OnRedirectToReturnUrl = redirect => AuthSessions.Refused(redirect.HttpContext) ? Task.CompletedTask : redirecting(redirect);
            var validating = cookie.Events.OnValidatePrincipal;
            cookie.Events.OnValidatePrincipal = async validate =>
            {
                await validating(validate);
                await AuthSessions.ValidateAsync(validate);
            };
        });
        services.Configure<SecurityStampValidatorOptions>(stamps => stamps.OnRefreshingPrincipal = AuthImpersonation.KeepClaims);
        new IdentityBuilder(typeof(User), typeof(Role), services).AddSignInManager();

        services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/endsession")
                    .SetUserInfoEndpointUris("/connect/userinfo");
                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().AllowClientCredentialsFlow();
                server.RequireProofKeyForCodeExchange();
                // plain sends the verifier itself through the browser: S256 only
                server.Configure(o =>
                {
                    o.CodeChallengeMethods.Clear();
                    o.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                });
                server.RegisterScopes([Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess, .. options.ApiScopes.Keys]);
                server.SetAccessTokenLifetime(options.AccessTokenLifetime);
                AddCertificates(server, options, context.Configuration);
                server.DisableAccessTokenEncryption();
                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();
                if (options.AllowHttp)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }

                if (options is { DynamicBackchannel: true, Issuer: not null })
                {
                    server.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(handler => handler
                        .UseInlineHandler(BrowserEndpointsByIssuer)
                        .SetOrder(OpenIddictServerHandlers.Discovery.AttachEndpoints.Descriptor.Order + 1_000));
                }
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();
            });
    }

    // OpenIddict names every endpoint by the address of the request; the pages the browser is sent to take the issuer's instead
    private static ValueTask BrowserEndpointsByIssuer(OpenIddictServerEvents.HandleConfigurationRequestContext context)
    {
        var issuer = context.Options.Issuer!;
        Uri? Browser(Uri? endpoint) => endpoint is null ? null
            : new UriBuilder(endpoint) { Scheme = issuer.Scheme, Host = issuer.Host, Port = issuer.Port }.Uri;
        context.AuthorizationEndpoint = Browser(context.AuthorizationEndpoint);
        context.EndSessionEndpoint = Browser(context.EndSessionEndpoint);
        return ValueTask.CompletedTask;
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
                $"Configure {AuthServerOptions.Section}:SigningCertificate and :EncryptionCertificate (Path or Base64, Password) for the auth server, or set {AuthServerOptions.Section}:DevelopmentCertificates for tests and demos.");
        }

        if (options.Issuer is { } issuer)
        {
            server.SetIssuer(issuer);
        }
    }

    // the one inline script allowed: OpenIddict's answer with response_mode=form_post submits its form to the client (ASP.NET Core's OpenID Connect handler, WebDataStudio)
    internal const string FormPostScriptHash = "'sha256-j7OoGArf6XW6YY4cAyS3riSSvrJRqpSi1fOF9vQ5SrI='";

    /// <summary>
    /// The account pages load their scripts from the own origin only (passkeys included) and style inline; after sign-in the browser follows redirects to the clients, which
    /// browsers check against form-action, so the clients' origins are allowed there. Logos and the design's image or video from elsewhere
    /// add their origins to img-src and media-src. The <see cref="SignInPreview"/> may be framed by the clients' origins as well.
    /// </summary>
    internal static string ContentSecurityPolicy(AuthServerOptions options, StoredClients stored, IReadOnlyCollection<string?>? mediaUrls = null, bool preview = false)
    {
        var clientOrigins = options.Clients.SelectMany(c => c.RedirectUris.Concat(c.PostLogoutRedirectUris)).Concat(stored.Uris)
            .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null).OfType<string>().ToList();
        var clients = clientOrigins.Concat(ExternalProviders.Origins(options.External)).Distinct(StringComparer.OrdinalIgnoreCase);
        mediaUrls ??= [];
        var logos = mediaUrls.Prepend(options.LogoUrl).Concat(options.Clients.Select(c => c.LogoUrl)).Concat(stored.Logos)
            .Where(Clients.ClientApp.IsWebAddress).Select(u => new Uri(u!).GetLeftPart(UriPartial.Authority)).Distinct(StringComparer.OrdinalIgnoreCase);
        var media = mediaUrls.Where(Clients.ClientApp.IsWebAddress).Select(u => new Uri(u!).GetLeftPart(UriPartial.Authority)).Distinct(StringComparer.OrdinalIgnoreCase);
        var ancestors = options.FrameAncestors.Select(o => Uri.TryCreate(o, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null)
            .OfType<string>().Concat(preview ? clientOrigins : []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return $"default-src 'self'; script-src 'self' {FormPostScriptHash}; style-src 'self' 'unsafe-inline'; img-src {string.Join(' ', ["'self'", "data:", .. logos])}; media-src {string.Join(' ', ["'self'", .. media])}; "
            + "object-src 'none'; base-uri 'self'; "
            + $"frame-ancestors {(ancestors.Count == 0 ? "'none'" : string.Join(' ', ancestors))}; form-action {string.Join(' ', ["'self'", .. clients])}";
    }

    // clients added in the admin pages redirect to their origins as well; the auth server learns about them within a minute
    internal static async Task<StoredClients> StoredClientsAsync(Microsoft.AspNetCore.Http.HttpContext context)
    {
        try
        {
            return await LoadStoredClientsAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // before the database is migrated
            return StoredClients.None;
        }
    }

    private static async Task<StoredClients> LoadStoredClientsAsync(Microsoft.AspNetCore.Http.HttpContext context) =>
        await Coworkee.Application.Caching.SharedCacheLoad.GetOrCreateInScopeAsync(
            context.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>(),
            context.RequestServices,
            new Coworkee.Core.Security.ImpersonatedUser(null, null),
            "coworkee:auth-clients",
            async (scope, ct) =>
            {
                var applications = scope.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();
                var uris = new List<string>();
                var logos = new List<string>();
                await foreach (var application in applications.ListAsync(null, null, ct))
                {
                    uris.AddRange(await applications.GetRedirectUrisAsync(application, ct));
                    uris.AddRange(await applications.GetPostLogoutRedirectUrisAsync(application, ct));
                    if (Clients.ClientApp.Read(await applications.GetPropertiesAsync(application, ct)).LogoUrl is { } logo)
                    {
                        logos.Add(logo);
                    }
                }

                return new StoredClients([.. uris], [.. logos]);
            },
            new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1) },
            cancellationToken: context.RequestAborted);

    // only the account pages show the design; administrators may change its media in the app
    private static async Task<string?[]> MediaUrlsAsync(Microsoft.AspNetCore.Http.HttpContext context, AuthServerOptions options)
    {
        if (!context.Request.Path.StartsWithSegments("/Account"))
        {
            return [];
        }

        try
        {
            var design = (await context.RequestServices.GetRequiredService<AuthBrandingProvider>().GetAsync(context.RequestAborted)).Design;
            return [design.MediaUrl, design.BackgroundUrl];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // before the database is migrated
            return [options.Design.MediaUrl, options.Design.BackgroundUrl];
        }
    }

    internal static X509Certificate2 Load(CertificateOptions certificate) => certificate switch
    {
        { Base64: { Length: > 0 } base64 } => X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(base64), certificate.Password, X509KeyStorageFlags.EphemeralKeySet),
        { Path: { Length: > 0 } path } => X509CertificateLoader.LoadPkcs12FromFile(path, certificate.Password, X509KeyStorageFlags.EphemeralKeySet),
        _ => throw new InvalidOperationException("A certificate needs a Path or Base64."),
    };

    public void ConfigureApplication(WebApplication app)
    {
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthServerOptions>>().Value;
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            var preview = SignInPreview.IsPreview(context);
            if (preview && !SignInPreview.TryStart(context))
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound;
                return;
            }

            if (preview)
            {
                // the sign-in form's antiforgery token adds X-Frame-Options: SAMEORIGIN, which no frame of the app's origin passes; frame-ancestors decides instead
                context.Response.OnStarting(() =>
                {
                    headers.Remove("X-Frame-Options");
                    return Task.CompletedTask;
                });
            }

            headers.ContentSecurityPolicy = ContentSecurityPolicy(options, await StoredClientsAsync(context), await MediaUrlsAsync(context, options), preview);
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
