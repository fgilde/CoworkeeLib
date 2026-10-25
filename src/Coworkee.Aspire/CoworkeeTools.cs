namespace Aspire.Hosting;

/// <summary>The tools of the app host by name, for <see cref="CoworkeeAppOptions.DeployTool"/> and <c>Coworkee:Deploy:Tools:{tool}</c>.</summary>
public static class CoworkeeTools
{
    public const string Keycloak = "keycloak";
    public const string Mailpit = "mailpit";
    public const string LibreTranslate = "libretranslate";
    public const string Grafana = "grafana";
    public const string WebDataStudio = "webdatastudio";
    public const string Ollama = "ollama";
    public const string AspireUI = "aspireui";
    public const string Kibana = "kibana";
    public const string OpenWebUI = "openwebui";

    /// <summary>The workflow designer, a project of the app (<c>MyApp.ElsaStudio</c>) that <see cref="CoworkeeApp.AddProjects"/> adds.</summary>
    public const string ElsaStudio = "elsastudio";

    public static readonly IReadOnlyList<string> All = [Keycloak, Mailpit, LibreTranslate, Grafana, WebDataStudio, Ollama, AspireUI, Kibana, OpenWebUI, ElsaStudio];

    /// <summary>The loopback port Docker Compose binds a tool's web interface to, next to the services' 8080 and up.</summary>
    internal static int ComposePort(string tool) => tool switch
    {
        Keycloak => 8090,
        Grafana => 8091,
        WebDataStudio => 8092,
        AspireUI => 8093,
        Mailpit or "mail" => 8094,
        Kibana => 8096,
        OpenWebUI => 8097,
        _ => 8095,
    };
}
