using Coworkee.Bff;
using Elsa.Studio.Contracts;
using Elsa.Studio.Core.BlazorServer.Extensions;
using Elsa.Studio.Dashboard.Extensions;
using Elsa.Studio.Extensions;
using Elsa.Studio.Models;
using Elsa.Studio.Shell.Extensions;
using Elsa.Studio.Workflows.Dashboard.Extensions;
using Elsa.Studio.Workflows.Designer.Extensions;
using Elsa.Studio.Workflows.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Elsa.Studio;

/// <summary>
/// Elsa Studio as an app of its own: users sign in at the Coworkee auth server like in the web app (a client of its own, <c>Coworkee:Bff</c>),
/// the designer calls the Elsa API of the app's api with their access token, which lets only users with <c>Workflows.Elsa.Manage</c> in.
/// </summary>
public static class ElsaStudioExtensions
{
    /// <summary>The Elsa API the designer works with, e.g. <c>https://api.example.com/api/elsa</c> (Aspire sets it).</summary>
    public const string BackendUrlSetting = "Coworkee:Workflows:Elsa:Studio:BackendUrl";

    internal const string PermissionCheckClient = "coworkee-elsa-studio-permission";

    public static WebApplicationBuilder AddCoworkeeElsaStudio(this WebApplicationBuilder builder)
    {
        var backend = BackendUrl(builder.Configuration);
        builder.AddCoworkeeBff();
        var services = builder.Services;
        services.AddRazorPages();
        services.AddServerSideBlazor(options =>
        {
            options.RootComponents.RegisterCustomElsaStudioElements();
            options.RootComponents.MaxJSRootComponents = 1000;
        });
        services.AddHttpContextAccessor();
        services.AddTransient<AccessTokenHandler>();
        services.AddScoped<IUnauthorizedComponentProvider, SignInComponentProvider>();
        services.AddScoped<global::Elsa.Studio.Authentication.Abstractions.Contracts.IHttpConnectionOptionsConfigurator, AccessTokenConnectionConfigurator>();
        services.AddHttpClient(PermissionCheckClient);

        var api = new BackendApiConfig
        {
            ConfigureBackendOptions = options => options.Url = backend,
            ConfigureHttpClientBuilder = options => options.AuthenticationHandler = typeof(AccessTokenHandler),
        };
        services.AddCore();
        services.AddShell();
        services.AddRemoteBackend(api);
        services.AddDashboardModule(api);
        services.AddWorkflowsModule(api);
        services.AddWorkflowsDashboardModule();
        return builder;
    }

    public static WebApplication MapCoworkeeElsaStudio(this WebApplication app)
    {
        app.UseStaticFiles();
        app.MapStaticAssets();
        app.MapCoworkeeBff();
        app.MapBlazorHub();
        app.MapFallbackToAreaPage("/Host", "ElsaStudio");
        return app;
    }

    internal static Uri BackendUrl(IConfiguration configuration) =>
        Uri.TryCreate(configuration[BackendUrlSetting]?.TrimEnd('/'), UriKind.Absolute, out var url) ? url
        : throw new InvalidOperationException($"Set {BackendUrlSetting} to the Elsa API of the app, e.g. https://api.example.com/api/elsa.");
}
