using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Modules;
using Coworkee.Aspire.Settings;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private (IResourceBuilder<ParameterResource> Public, IResourceBuilder<ParameterResource> Private)? _pushKeys;

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

    // Mailpit is a development tool; without UseMail a deployment sends through the mail server of its deploy parameters, or through Mailpit when switched on
    private void WireMail(IResourceBuilder<ProjectResource> service)
    {
        if (Publishing && Options.Smtp is null && !Deploys(CoworkeeTools.Mailpit))
        {
            foreach (var (setting, parameter) in SmtpParameters())
            {
                service.WithSetting(s => s.Coworkee.Settings.Defaults[setting], parameter);
            }

            return;
        }

        var smtp = Smtp;
        service.WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"], ReferenceExpression.Create($"{smtp.Property(EndpointProperty.Host)}"))
            .WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Port"], ReferenceExpression.Create($"{smtp.Property(Publishing ? EndpointProperty.TargetPort : EndpointProperty.Port)}"));
        if (Options.Smtp is null)
        {
            service.WaitFor(Mail);
        }
    }

    private (string Setting, IResourceBuilder<ParameterResource> Parameter)[] SmtpParameters() =>
    [
        ("Mail.Smtp.Host", DeployParameter($"{Name}-smtp-host").WithDescription("The SMTP server the app sends its mails through")),
        ("Mail.Smtp.Port", DeployParameter($"{Name}-smtp-port")),
        ("Mail.Smtp.UserName", DeployParameter($"{Name}-smtp-username")),
        ("Mail.Smtp.Password", DeployParameter($"{Name}-smtp-password", secret: true)),
        ("Mail.From", DeployParameter($"{Name}-smtp-from").WithDescription("The sender address of the app's mails")),
    ];

    // a host path means nothing inside a published container; there the files go to a volume shared by the services
    private void WireStorage(IResourceBuilder<ProjectResource> service)
    {
        if (Builder.ExecutionContext.IsRunMode)
        {
            service.WithSetting(s => s.Coworkee.Storage.FileSystem.Root, BlobRoot);
        }
        else
        {
            WithFileVolume(service);
        }
    }

    // search may start later: the index is created on first use and index jobs retry
    private void WireSearch(IResourceBuilder<ProjectResource> service) =>
        service.WithReference(Options.Search ?? Search, "elasticsearch");

    // links in digest mails lead to the web app, which is added last
    private void WireNotifications(IResourceBuilder<ProjectResource> service)
    {
        service.WithEnvironment(context =>
        {
            if (Web is { } web)
            {
                context.EnvironmentVariables[SettingPath.Of(s => s.Coworkee.Notifications.PublicAppUrl)] = PublicUrl(web);
            }
        });
        if (Options.Push)
        {
            var (publicKey, privateKey) = _pushKeys ??= PushKeys();
            service.WithSetting(s => s.Coworkee.Notifications.Push.PublicKey, publicKey)
                .WithSetting(s => s.Coworkee.Notifications.Push.PrivateKey, privateKey);
        }
    }

    // one VAPID key pair for every service, made at the first start and kept in the app host's user secrets
    private (IResourceBuilder<ParameterResource> Public, IResourceBuilder<ParameterResource> Private) PushKeys()
    {
        var pair = new Lazy<(string Public, string Private)>(VapidKeyDefault.Create);
        return (Builder.AddParameter($"{Name}-push-public-key", new VapidKeyDefault(pair, false), secret: false, persist: true),
            Builder.AddParameter($"{Name}-push-private-key", new VapidKeyDefault(pair, true), secret: true, persist: true));
    }
}
