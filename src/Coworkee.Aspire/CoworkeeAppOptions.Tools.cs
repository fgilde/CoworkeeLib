using Aspire.Hosting.ApplicationModel;
using Nextended.Aspire.Hosting.Grafana;
using Nextended.Aspire.Hosting.WebDataStudio;

namespace Aspire.Hosting;

public sealed partial class CoworkeeAppOptions
{
    /// <summary>The app's administrator; Grafana and WebDataStudio take the same sign-in. Without it they get the user <c>admin</c> and a generated password (a parameter in the dashboard).</summary>
    public CoworkeeLogin? Administrator { get; set; }

    internal Action<IResourceBuilder<GrafanaResource>>? GrafanaServer { get; private set; }

    internal Action<IResourceBuilder<WebDataStudioResource>>? WebDataStudioServer { get; private set; }

    /// <summary>Grafana with Prometheus, Loki, Tempo and an OpenTelemetry collector: the services' metrics, logs and traces, the app database and ready dashboards. Development only; with Keycloak it offers single sign-on.</summary>
    public CoworkeeAppOptions UseGrafana(Action<IResourceBuilder<GrafanaResource>>? configure = null) =>
        Set(() => GrafanaServer = (GrafanaServer ?? (_ => { })) + configure);

    public CoworkeeAppOptions WithoutGrafana() => Set(() => GrafanaServer = null);

    /// <summary>WebDataStudio, a database studio in the browser, connected to the app database. Development only; with Keycloak it offers single sign-on.</summary>
    public CoworkeeAppOptions UseWebDataStudio(Action<IResourceBuilder<WebDataStudioResource>>? configure = null) =>
        Set(() => WebDataStudioServer = (WebDataStudioServer ?? (_ => { })) + configure);

    public CoworkeeAppOptions WithoutWebDataStudio() => Set(() => WebDataStudioServer = null);
}
