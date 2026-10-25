using Aspire.Hosting.ApplicationModel;
using Nextended.Aspire.Hosting.Grafana;
using Nextended.Aspire.Hosting.WebDataStudio;

namespace Aspire.Hosting;

public sealed partial class CoworkeeAppOptions
{
    /// <summary>
    /// The app's administrator while developing; Grafana, WebDataStudio and AspireUI take the same sign-in. Without it they get the user <c>admin</c> and a generated password
    /// (a parameter in the dashboard). A deployment takes the parameters <c>{app}-admin-email</c> and <c>-admin-password</c> instead.
    /// </summary>
    public CoworkeeLogin? Administrator { get; set; }

    internal Action<IResourceBuilder<GrafanaResource>>? GrafanaServer { get; private set; }

    internal Action<IResourceBuilder<WebDataStudioResource>>? WebDataStudioServer { get; private set; }

    /// <summary>Grafana with Prometheus, Loki, Tempo and an OpenTelemetry collector: the services' metrics, logs and traces, the app database and ready dashboards. With Keycloak it offers single sign-on; deployed unless <see cref="CoworkeeTools.Grafana"/> is switched off.</summary>
    public CoworkeeAppOptions UseGrafana(Action<IResourceBuilder<GrafanaResource>>? configure = null) =>
        Set(() => GrafanaServer = (GrafanaServer ?? (_ => { })) + configure);

    public CoworkeeAppOptions WithoutGrafana() => Set(() => GrafanaServer = null);

    /// <summary>WebDataStudio, a database studio in the browser, connected to the app database. With Keycloak it offers single sign-on; deployed unless <see cref="CoworkeeTools.WebDataStudio"/> is switched off.</summary>
    public CoworkeeAppOptions UseWebDataStudio(Action<IResourceBuilder<WebDataStudioResource>>? configure = null) =>
        Set(() => WebDataStudioServer = (WebDataStudioServer ?? (_ => { })) + configure);

    public CoworkeeAppOptions WithoutWebDataStudio() => Set(() => WebDataStudioServer = null);

    internal Action<IResourceBuilder<ContainerResource>>? KibanaServer { get; private set; }

    /// <summary>
    /// Kibana next to the Elasticsearch container of the search module (not with <see cref="UseSearch"/>). Behind oauth2-proxy the browser signs in at the app's auth server
    /// (with Keycloak when it is on); only administrators of the system organisation get in. Deployed unless <see cref="CoworkeeTools.Kibana"/> is switched off.
    /// </summary>
    public CoworkeeAppOptions UseKibana(Action<IResourceBuilder<ContainerResource>>? configure = null) =>
        Set(() => KibanaServer = (KibanaServer ?? (_ => { })) + configure);

    public CoworkeeAppOptions WithoutKibana() => Set(() => KibanaServer = null);
}
