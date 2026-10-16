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

    internal Dictionary<string, bool> DeployedTools { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a deployment takes a tool of <see cref="CoworkeeTools"/> along: every tool the app host uses is deployed, Mailpit only when asked
    /// (a deployment sends through a real mail server). The configuration <c>Coworkee:Deploy:Tools:{tool}</c> (<c>coworkee deploy --without grafana</c>) wins.
    /// </summary>
    public CoworkeeAppOptions DeployTool(string tool, bool deploy = true) => Set(() => DeployedTools[tool] = deploy);

    /// <summary>Publishes to Docker Compose: <c>aspire publish</c> writes a docker-compose.yaml with all services and containers.</summary>
    public CoworkeeAppOptions PublishToDockerCompose(Action<IResourceBuilder<DockerComposeEnvironmentResource>>? configure = null) =>
        Set(() => DockerCompose = configure ?? (_ => { }));
}
