using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Modules;
using Coworkee.Aspire.Settings;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private static OrderedDictionary<string, Action<CoworkeeApp, IResourceBuilder<ProjectResource>>> BuiltInModules() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [CoworkeeModules.Infrastructure] = (app, service) => app.WireDatabase(service),
            [CoworkeeModules.Realtime] = (app, service) => app.WireRedis(service),
            [CoworkeeModules.Mailing] = (app, service) => app.WireMail(service),
            [CoworkeeModules.Storage] = (app, service) => app.WireStorage(service),
            [CoworkeeModules.Search] = (app, service) => app.WireSearch(service),
            [CoworkeeModules.Notifications] = (app, service) => app.WireNotifications(service),
            [CoworkeeModules.Localization] = (app, service) => app.WireTranslation(service),
        };

    private void WireDatabase(IResourceBuilder<ProjectResource> service)
    {
        WithAppDatabase(service, awaitServer: Migrations is null);
        if (Migrations is { } migrations)
        {
            service.WaitForCompletion(migrations);
        }
    }

    private void WireRedis(IResourceBuilder<ProjectResource> service)
    {
        if (Options.Redis is { } redis)
        {
            service.WithReference(redis, "redis");
            return;
        }

        service.WithReference(Redis).WaitFor(Redis);
    }

    private void WireMail(IResourceBuilder<ProjectResource> service)
    {
        var smtp = Smtp;
        service.WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"], ReferenceExpression.Create($"{smtp.Property(EndpointProperty.Host)}"))
            .WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Port"], ReferenceExpression.Create($"{smtp.Property(EndpointProperty.Port)}"));
        if (Options.Smtp is null)
        {
            service.WaitFor(Mail);
        }
    }

    // a host path means nothing inside a published container; there the service keeps its own default
    private void WireStorage(IResourceBuilder<ProjectResource> service)
    {
        if (Builder.ExecutionContext.IsRunMode)
        {
            service.WithSetting(s => s.Coworkee.Storage.FileSystem.Root, BlobRoot);
        }
    }

    // search may start later: the index is created on first use and index jobs retry
    private void WireSearch(IResourceBuilder<ProjectResource> service) =>
        service.WithReference(Options.Search ?? Search, "elasticsearch");

    // links in digest mails lead to the web app, which is added last
    private void WireNotifications(IResourceBuilder<ProjectResource> service) =>
        service.WithEnvironment(context =>
        {
            if (Web is { } web)
            {
                context.EnvironmentVariables[SettingPath.Of(s => s.Coworkee.Notifications.PublicAppUrl)] = web.GetEndpoint("https");
            }
        });
}
