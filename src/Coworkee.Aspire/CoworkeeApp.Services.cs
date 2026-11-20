using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure.AppContainers;
using Coworkee.Aspire.Modules;
using Coworkee.Contracts.Configuration;
using Coworkee.Aspire.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nextended.Aspire.Hosting.AspireUI;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private readonly HashSet<IResource> _services = [];

    /// <summary>The Aspire dashboard, the jobs dashboard and every resource with an http endpoint, under Coworkee:Services (CoworkeeServicesOptions).</summary>
    private void WireServices(IResourceBuilder<ProjectResource> service)
    {
        _services.Add(service.Resource);

        // published endpoints are internal addresses; a deployment lists the public addresses of its tools
        if (Publishing)
        {
            service.WithEnvironment(context =>
            {
                foreach (var (tool, url) in _toolUrls)
                {
                    context.EnvironmentVariables[SettingPath.Of(s => s.Coworkee.Services[tool.Name].Url)] = url;
                }

                if (DeployedDashboard() is { } dashboard)
                {
                    context.EnvironmentVariables[SettingPath.Of(s => s.Coworkee.Services[CoworkeeServicesOptions.Dashboard].Url)] = dashboard;
                }
            });
            return;
        }

        // read when the service starts, so resources added after the app count too
        service.WithEnvironment(async context =>
        {
            foreach (var (name, url, health) in ServiceUrls())
            {
                context.EnvironmentVariables[SettingPath.Of(s => s.Coworkee.Services[name].Url)] = url;
                if (health is not null)
                {
                    context.EnvironmentVariables[SettingPath.Of(s => s.Coworkee.Services[name].HealthPath)] = health;
                }
            }

            await AddAspireUIAsync(context);
        });
    }

    // ponytail: the first AspireUI with the retrieve API wins
    private async Task AddAspireUIAsync(EnvironmentCallbackContext context)
    {
        foreach (var aspireUi in Builder.Resources.OfType<AspireUIResource>())
        {
            var env = new Dictionary<string, object>();
            var aspireUiContext = new EnvironmentCallbackContext(context.ExecutionContext, aspireUi, env, context.CancellationToken) { Logger = context.Logger ?? NullLogger.Instance };
            try
            {
                foreach (var callback in aspireUi.Annotations.OfType<EnvironmentCallbackAnnotation>())
                {
                    await callback.Callback(aspireUiContext);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                context.Logger?.LogWarning(exception, "The AspireUI {Name} could not be read, it is left out of the service directory", aspireUi.Name);
                continue;
            }

            if (env.GetValueOrDefault("ASPIREUI_RETRIEVE_API") is not "true")
            {
                continue;
            }

            context.EnvironmentVariables[SettingPath.Of(s => s.Coworkee.AspireUI.Url)] = aspireUi.GetEndpoint(AspireUIResource.HttpEndpointName);
            if (env.TryGetValue("ASPIREUI_RETRIEVE_API_KEY", out var key))
            {
                context.EnvironmentVariables[SettingPath.Of(s => s.Coworkee.AspireUI.ApiKey)] = key;
            }

            return;
        }
    }

    /// <summary>
    /// The Aspire dashboard of a deployment: Container Apps environments run it as their component <c>aspire-dashboard</c> (on unless
    /// <c>WithDashboard(false)</c> or Express) at <c>https://aspire-dashboard.ext.{default domain}</c>, the address Aspire prints after a deploy. Docker
    /// Compose and Kubernetes run a dashboard too, but only on a random host port or inside the cluster: no address browsers could use.
    /// </summary>
    private ReferenceExpression? DeployedDashboard()
    {
        if (Builder.Resources.OfType<AzureContainerAppEnvironmentResource>().FirstOrDefault(HasDashboard) is not { } environment)
        {
            return null;
        }

        var domain = new global::Aspire.Hosting.Azure.BicepOutputReference("AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN", environment);
        return ReferenceExpression.Create($"https://aspire-dashboard.ext.{domain}");
    }

    // ponytail: Aspire 17 keeps EnableDashboard internal (WithDashboard sets it); read by reflection, Aspire's default (on) when it moves
    private static readonly System.Reflection.PropertyInfo? EnableDashboard = typeof(AzureContainerAppEnvironmentResource)
        .GetProperty("EnableDashboard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

    private static bool HasDashboard(AzureContainerAppEnvironmentResource environment) => EnableDashboard?.GetValue(environment) as bool? ?? true;

    /// <summary>
    /// The dashboard of a local run: the app host's ASPNETCORE_URLS (the launch profile's applicationUrl; without it Aspire picks a random port and the
    /// services get none), https first, with the browser token, so the link signs in like the one on the console.
    /// </summary>
    internal static string? LocalDashboard(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var url = configuration["ASPNETCORE_URLS"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderByDescending(u => u.StartsWith("https:", StringComparison.OrdinalIgnoreCase)).FirstOrDefault()?.TrimEnd('/');
        return url is null ? null
            : configuration["AppHost:BrowserToken"] is { Length: > 0 } token ? $"{url}/login?t={Uri.EscapeDataString(token)}"
            : url;
    }

    private IEnumerable<(string Name, object Url, string? Health)> ServiceUrls()
    {
        if (LocalDashboard(Builder.Configuration) is { } dashboard)
        {
            yield return (CoworkeeServicesOptions.Dashboard, dashboard, null);
        }

        if (Web is { } web && _apis.Any(a => CoworkeeModules.Of(a.Resource).Contains(CoworkeeModules.BackgroundJobs)))
        {
            yield return ("jobs", ReferenceExpression.Create($"{web.GetEndpoint("https")}/admin/jobs"), null);
        }

        foreach (var resource in Builder.Resources.OfType<IResourceWithEndpoints>().Where(r => r != Migrations?.Resource && r.Name != "aspire-dashboard"))
        {
            var endpoint = resource.Annotations.OfType<EndpointAnnotation>()
                .Where(e => e.UriScheme is "http" or "https")
                .OrderByDescending(e => e.UriScheme == "https")
                .FirstOrDefault();
            if (endpoint is not null)
            {
                yield return (resource.Name, new EndpointReference(resource, endpoint), _services.Contains(resource) ? Health : null);
            }
        }
    }
}
