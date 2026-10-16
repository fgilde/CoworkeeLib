using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure.AppContainers;
using Aspire.Hosting.Docker;
using Coworkee.Aspire.Settings;
using Microsoft.Extensions.Configuration;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    /// <summary>What <c>aspire publish</c> writes when the app host adds no environment itself: <c>compose</c> (default), <c>azure</c> or <c>k8s</c>, e.g. <c>coworkee deploy k8s</c>.</summary>
    public const string DeployTargetSetting = "Coworkee:Deploy:Target";

    public static readonly IReadOnlyList<string> DeployTargets = ["compose", "azure", "k8s"];

    /// <summary>Per tool switch of a deployment, e.g. <c>Coworkee:Deploy:Tools:grafana=false</c> (see <see cref="CoworkeeAppOptions.DeployTool"/>).</summary>
    public const string DeployToolsSetting = "Coworkee:Deploy:Tools";

    private bool Publishing => Builder.ExecutionContext.IsPublishMode;

    private void AddDeployTarget()
    {
        if (!Publishing)
        {
            if (Options.DockerCompose is { } configure)
            {
                configure(Builder.AddDockerComposeEnvironment("compose"));
            }

            return;
        }

        // an environment of the app host's own (added before the app) wins
        if (Builder.Resources.OfType<IComputeEnvironmentResource>().Any())
        {
            return;
        }

        _target = Builder.Configuration[DeployTargetSetting]?.ToLowerInvariant() ?? "compose";
        switch (_target)
        {
            case "compose":
                (Options.DockerCompose ?? (_ => { }))(Builder.AddDockerComposeEnvironment("compose"));
                break;
            case "azure":
                Builder.AddAzureContainerAppEnvironment("aca");
                break;
            case "k8s":
                Builder.AddKubernetesEnvironment("k8s");
                break;
            case var other:
                throw new InvalidOperationException($"{DeployTargetSetting} '{other}' is unknown, use one of {string.Join(", ", DeployTargets)}.");
        }
    }

    private const int ContainerPort = 8080;

    private readonly Dictionary<IResource, IResourceBuilder<ParameterResource>> _publicUrls = [];
    private int _hostPort = 8080;
    private string? _target;

    /// <summary>
    /// The address browsers use for a service: its https endpoint when the app host runs, a parameter <c>{service}-url</c> when it publishes
    /// (e.g. <c>https://auth.example.com</c>, the address of the reverse proxy in front of it).
    /// </summary>
    public ReferenceExpression PublicUrl(IResourceBuilder<ProjectResource> service)
    {
        if (!Publishing)
        {
            return ReferenceExpression.Create($"{service.GetEndpoint("https")}");
        }

        if (!_publicUrls.TryGetValue(service.Resource, out var url))
        {
            _publicUrls[service.Resource] = url = DeployParameter($"{service.Resource.Name}-url")
                .WithDescription($"The public address of {service.Resource.Name}, e.g. https://{service.Resource.Name}.example.com");
        }

        return ReferenceExpression.Create($"{url}");
    }

    /// <summary>
    /// Whether the app host adds <paramref name="tool"/> (a name of <see cref="CoworkeeTools"/>): always while it runs; in a deployment unless switched off,
    /// Mailpit only when switched on, AspireUI by default on Docker Compose only (it drives the host's Docker through its socket).
    /// </summary>
    public bool Deploys(string tool) =>
        !Publishing || (Builder.Configuration.GetValue<bool?>($"{DeployToolsSetting}:{tool}") ?? Options.DeployedTools.GetValueOrDefault(tool, DeployedByDefault(tool)));

    private bool DeployedByDefault(string tool) => tool switch
    {
        CoworkeeTools.Mailpit => false,
        CoworkeeTools.AspireUI => _target is not ("azure" or "k8s"),
        _ => true,
    };

    private readonly Dictionary<IResource, ReferenceExpression> _toolUrls = [];

    /// <summary>
    /// The address browsers use for a tool's web interface: its endpoint while running, in a deployment a parameter <c>{app}-{tool}-url</c>
    /// (the reverse proxy in front of the loopback port of <see cref="CoworkeeTools.ComposePort"/>).
    /// </summary>
    public ReferenceExpression ToolUrl<T>(IResourceBuilder<T> tool, string endpointName = "http")
        where T : ContainerResource
    {
        if (!Publishing)
        {
            return ReferenceExpression.Create($"{tool.GetEndpoint(endpointName)}");
        }

        if (!_toolUrls.TryGetValue(tool.Resource, out var url))
        {
            var name = tool.Resource.Name;
            var parameter = DeployParameter($"{Name}-{name}-url").WithDescription($"The public address of {name}, e.g. https://{name}.example.com");
            _toolUrls[tool.Resource] = url = ReferenceExpression.Create($"{parameter}");
            tool.WithEndpoint(endpointName, e => e.IsExternal = true)
                // tools behind the proxy build their sign-in addresses from the forwarded scheme and host
                .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true");
            if (Builder.Resources.OfType<DockerComposeEnvironmentResource>().Any())
            {
                var port = CoworkeeTools.ComposePort(name);
                var target = tool.Resource.Annotations.OfType<EndpointAnnotation>().First(e => e.Name == endpointName).TargetPort;
                tool.PublishAsDockerComposeService((_, compose) =>
                {
                    compose.Ports.Clear();
                    compose.Ports.Add($"127.0.0.1:{port}:{target}");
                });
            }
        }

        return url;
    }

    private IResourceBuilder<ParameterResource>? _adminEmail;

    /// <summary>The app's administrator: <see cref="CoworkeeAppOptions.Administrator"/> while running, the parameters <c>{app}-admin-email</c> and <c>-admin-password</c> in a deployment.</summary>
    private ReferenceExpression AdminEmail => Publishing ? ReferenceExpression.Create($"{AdminEmailParameter}") : ReferenceExpression.Create($"{AdminUser}");

    private IResourceBuilder<ParameterResource> AdminEmailParameter =>
        _adminEmail ??= DeployParameter($"{Name}-admin-email").WithDescription("The first administrator of the app and its tools");

    // the migrations set an empty database up with the administrator; without the parameters the setup page and its token do
    private void WithInitialAdmin(IResourceBuilder<ProjectResource> migrations)
    {
        if (Publishing)
        {
            migrations.WithEnvironment("Coworkee__InitialAdmin__Email", AdminEmail)
                .WithEnvironment("Coworkee__InitialAdmin__Password", AdminPassword)
                .WithEnvironment("Coworkee__InitialAdmin__LastName", Title);
        }
    }

    // tokens name the public address as issuer and are signed with certificates every instance shares (coworkee deploy generates them)
    private void WithProductionAuth(IResourceBuilder<ProjectResource> auth) =>
        auth.WithSetting(s => s.Coworkee.Auth.Issuer, PublicUrl(auth))
            .WithSetting(s => s.Coworkee.Auth.SigningCertificate!.Base64, DeployParameter($"{Name}-signing-certificate", secret: true))
            .WithSetting(s => s.Coworkee.Auth.EncryptionCertificate!.Base64, DeployParameter($"{Name}-encryption-certificate", secret: true));

    // /home/app belongs to the app user of the .NET images, so a new volume mounted there is writable for it
    private void WithFileVolume(IResourceBuilder<ProjectResource> service)
    {
        service.WithAnnotation(new ContainerMountAnnotation($"{Name}-files", "/home/app", ContainerMountType.Volume, isReadOnly: false))
            .WithSetting(s => s.Coworkee.Storage.FileSystem.Root, "/home/app/blobs");
    }

    /// <summary>
    /// Services browsers reach listen on 8080 in their containers; Docker Compose binds them to the host's loopback (8080, 8081, ... in the order
    /// they were added) for the reverse proxy in front.
    /// </summary>
    private IResourceBuilder<ProjectResource> Exposed(IResourceBuilder<ProjectResource> service)
    {
        if (Publishing)
        {
            // TLS ends at the proxy or the ingress; one plain http endpoint is what Container Apps and Kubernetes take
            foreach (var https in service.Resource.Annotations.OfType<EndpointAnnotation>().Where(e => e.UriScheme == "https").ToList())
            {
                service.Resource.Annotations.Remove(https);
            }
        }

        service.WithExternalHttpEndpoints();
        if (Publishing)
        {
            service.WithEndpoint("http", e => e.TargetPort = ContainerPort);
            if (Builder.Resources.OfType<DockerComposeEnvironmentResource>().Any())
            {
                var hostPort = _hostPort++;
                service.PublishAsDockerComposeService((_, compose) =>
                {
                    compose.Ports.Clear();
                    compose.Ports.Add($"127.0.0.1:{hostPort}:{ContainerPort}");
                });
            }
        }

        return service;
    }

    // Compose leaves a parameter without value to the .env; the Azure and Kubernetes publishers would wait for one, so values.yaml and the Bicep parameters start empty
    private IResourceBuilder<ParameterResource> DeployParameter(string name, bool secret = false) =>
        Builder.Resources.OfType<ParameterResource>().FirstOrDefault(p => p.Name == name) is { } existing ? Builder.CreateResourceBuilder(existing) :
        _target is "k8s" or "azure" ? Builder.AddParameter(name, string.Empty, secret: secret) : Builder.AddParameter(name, secret: secret);

    /// <summary>A secret the app host keeps in the user secrets while running; a deployment takes it as parameter (coworkee deploy generates it).</summary>
    private IResourceBuilder<ParameterResource> GeneratedSecret(string name, int length) => Publishing
        ? DeployParameter(name, secret: true)
        : Builder.AddParameter(name, new GenerateParameterDefault { MinLength = length, Special = false }, secret: true, persist: true);

    // Container Apps would restart the finished migrations forever; as a job they run when started (az containerapp job start), Kubernetes gets a Job from coworkee deploy
    private void AsDeployJob(IResourceBuilder<ProjectResource> migrations)
    {
        if (Publishing && Builder.Resources.OfType<AzureContainerAppEnvironmentResource>().Any())
        {
            // a job has no ingress; endpoints of a web project template would point nowhere
            foreach (var endpoint in migrations.Resource.Annotations.OfType<EndpointAnnotation>().ToList())
            {
                migrations.Resource.Annotations.Remove(endpoint);
            }

            migrations.PublishAsAzureContainerAppJob();
        }
    }
}
