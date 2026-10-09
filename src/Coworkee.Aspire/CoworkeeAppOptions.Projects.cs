using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker;

namespace Aspire.Hosting;

public sealed partial class CoworkeeAppOptions
{
    internal Dictionary<string, Action<IResourceBuilder<ProjectResource>>> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal HashSet<string> Skipped { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal Action<IResourceBuilder<DockerComposeEnvironmentResource>>? DockerCompose { get; private set; }

    /// <summary>Configures a service by its suffix (<c>"api"</c> for <c>myapp-api</c>), however it was added.</summary>
    public CoworkeeAppOptions Configure(string suffix, Action<IResourceBuilder<ProjectResource>> configure)
    {
        Services[suffix] = Services.GetValueOrDefault(suffix) + configure;
        return this;
    }

    /// <summary>Projects that <see cref="CoworkeeApp.AddProjects"/> leaves out, by suffix (<c>"web"</c>) or type name (<c>"MyApp_Web"</c>).</summary>
    public CoworkeeAppOptions Skip(params string[] names) => Set(() => Skipped.UnionWith(names));

    /// <summary>Publishes to Docker Compose: <c>aspire publish</c> writes a docker-compose.yaml with all services and containers.</summary>
    public CoworkeeAppOptions PublishToDockerCompose(Action<IResourceBuilder<DockerComposeEnvironmentResource>>? configure = null) =>
        Set(() => DockerCompose = configure ?? (_ => { }));
}
