using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Modules;
using Coworkee.Aspire.Settings;
using Microsoft.Extensions.Logging;
using Nextended.Aspire.Hosting.AspireUI;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private readonly HashSet<IResource> _services = [];

    /// <summary>The Aspire dashboard, the jobs dashboard and every resource with an http endpoint, under Coworkee:Services (CoworkeeServicesOptions).</summary>
    private void WireServices(IResourceBuilder<ProjectResource> service)
    {
        _services.Add(service.Resource);

        // ponytail: run mode only, published endpoints are internal addresses; deployments list their public ones under Coworkee:Services
        if (!Builder.ExecutionContext.IsRunMode)
        {
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
            var aspireUiContext = new EnvironmentCallbackContext(context.ExecutionContext, env, context.CancellationToken);
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

    private IEnumerable<(string Name, object Url, string? Health)> ServiceUrls()
    {
        var dashboard = Builder.Configuration["ASPNETCORE_URLS"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderByDescending(u => u.StartsWith("https:", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (dashboard is not null)
        {
            yield return ("dashboard", dashboard, null);
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
