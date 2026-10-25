using Coworkee.Cli.Spec;

namespace Coworkee.Studio.Model;

/// <summary>What the new solution contains for a spec: projects, Aspire containers and Coworkee packages.</summary>
public sealed record SolutionSummary(IReadOnlyList<string> Projects, IReadOnlyList<string> Containers, IReadOnlyList<string> Packages)
{
    private static readonly string[] BaseProjects = ["Api", "AppHost", "Application", "Auth", "Contracts", "Infrastructure", "Migrations", "ServiceDefaults", "Web", "Web.Client"];
    private static readonly string[] TestProjects = ["Api.Tests", "AppHost.Tests", "Migrations.Tests", "Web.Client.Tests"];
    private static readonly string[] BasePackages =
        ["Coworkee.Core", "Coworkee.Domain", "Coworkee.Application", "Coworkee.Infrastructure", "Coworkee.AspNetCore", "Coworkee.Identity", "Coworkee.AuthServer", "Coworkee.Bff",
         "Coworkee.OData", "Coworkee.Settings", "Coworkee.Theming", "Coworkee.Localization", "Coworkee.Realtime", "Coworkee.Mailing", "Coworkee.BackgroundJobs", "Coworkee.Client.Blazor", "Coworkee.Aspire"];

    public static SolutionSummary For(AppSpec spec)
    {
        var values = SpecOptions.Resolve(spec);
        bool On(string id) => values[id] == "true";
        var name = string.IsNullOrWhiteSpace(spec.Name) ? "MyApp" : spec.Name;

        var projects = BaseProjects
            .Concat(On("samples") ? ["Catalog", "Documents"] : [])
            .Concat(On("airworks") ? ["AirWorks"] : [])
            .Concat(On("elsaStudio") ? ["ElsaStudio"] : [])
            .Concat(spec.Modules.Select(m => m.Name).Where(n => n.Length > 0))
            .Concat(On("tests") ? TestProjects : [])
            .Select(p => $"{name}.{p}")
            .ToList();

        // SQLite is a file, no container
        List<string> containers = [.. values["database"] switch { "sqlServer" => new[] { "SQL Server" }, "sqlite" => [], _ => ["PostgreSQL"] }, .. On("redis") ? ["Redis"] : Array.Empty<string>(), "Mailpit"];
        if (On("elasticsearch"))
        {
            containers.Add("Elasticsearch");
        }

        if (values["eventBus"] == "rabbitMq")
        {
            containers.Add("RabbitMQ");
        }

        if (values["storage"] == "azureBlob")
        {
            containers.Add("Azurite");
        }

        if (On("keycloak"))
        {
            containers.Add("Keycloak");
        }

        if (On("ai") && values["aiProvider"] == "ollama")
        {
            containers.Add("Ollama");
        }

        if (values["translationProvider"] == "libreTranslate")
        {
            containers.Add("LibreTranslate");
        }

        if (On("grafana"))
        {
            containers.AddRange(["Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector", .. values["database"] == "postgres" ? new[] { "postgres_exporter" } : []]);
        }

        if (On("webDataStudio"))
        {
            containers.Add("WebDataStudio");
        }

        if (On("elasticsearch") && On("kibana"))
        {
            containers.AddRange(["Kibana", "oauth2-proxy"]);
        }

        if (On("aspireUI"))
        {
            containers.Add("AspireUI");
        }

        var packages = BasePackages.Concat(OptionCatalog.All
                .Where(o => o.Package is not null && (o.Kind == OptionKind.Bool ? On(o.Id) : values[o.Id] is { } v && v != "none"))
                .Select(o => o.Package!))
            .Concat(values["eventBus"] == "rabbitMq" ? ["Coworkee.EventBus.RabbitMq"] : [])
            .Where(p => p != "Coworkee.Backup" || values["database"] == "postgres")
            .Distinct()
            .ToList();
        return new(projects, containers, packages);
    }
}
