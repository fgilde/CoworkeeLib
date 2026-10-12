using System.Text.Json.Nodes;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.Tests;

public sealed class SolutionSummaryTests
{
    public static TheoryData<string, string, bool, bool, bool> Combinations()
    {
        var data = new TheoryData<string, string, bool, bool, bool>();
        foreach (var database in new[] { "postgres", "sqlServer", "sqlite" })
        {
            foreach (var provider in OptionCatalog.Find("aiProvider")!.Choices!.Select(c => c.Value))
            {
                foreach (var ai in new[] { true, false })
                {
                    foreach (var redis in new[] { true, false })
                    {
                        data.Add(database, provider, ai, redis, database == "postgres" && !redis);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Containers_and_packages_follow_database_ai_provider_and_redis(string database, string provider, bool ai, bool redis, bool backup)
    {
        var spec = new AppSpec
        {
            Name = "Shop",
            Options = new Dictionary<string, JsonNode?>
            {
                ["database"] = database, ["aiProvider"] = provider, ["ai"] = ai, ["redis"] = redis, ["backup"] = backup,
            },
        };

        var summary = SolutionSummary.For(spec);

        SpecValidator.Validate(spec).ShouldBeEmpty();
        string[] expected =
        [
            .. database switch { "postgres" => ["PostgreSQL"], "sqlServer" => ["SQL Server"], _ => Array.Empty<string>() },
            .. redis ? ["Redis"] : Array.Empty<string>(),
            "Mailpit",
            .. ai && provider == "ollama" ? ["Ollama"] : Array.Empty<string>(),
            "LibreTranslate",
            "Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector",
            .. database == "postgres" ? ["postgres_exporter"] : Array.Empty<string>(),
            "WebDataStudio",
        ];
        summary.Containers.ShouldBe(expected);
        summary.Packages.Contains("Coworkee.Ai").ShouldBe(ai);
        summary.Packages.Contains("Coworkee.Backup").ShouldBe(backup);
    }

    [Fact]
    public void The_backup_default_is_left_out_off_postgres()
    {
        var spec = new AppSpec { Name = "Shop", Options = new Dictionary<string, JsonNode?> { ["database"] = "sqlite" } };

        SpecValidator.Validate(spec).ShouldBeEmpty();
        SolutionSummary.For(spec).Packages.ShouldNotContain("Coworkee.Backup");
    }

    [Theory]
    [InlineData("Minimal", new[] { "PostgreSQL", "Mailpit" })]
    [InlineData("Business app", new[] { "PostgreSQL", "Redis", "Mailpit", "LibreTranslate", "Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector", "postgres_exporter", "WebDataStudio" })]
    [InlineData("Collaboration", new[] { "PostgreSQL", "Redis", "Mailpit", "LibreTranslate", "Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector", "postgres_exporter", "WebDataStudio" })]
    [InlineData("SaaS platform", new[] { "PostgreSQL", "Redis", "Mailpit", "RabbitMQ", "Azurite", "Keycloak", "LibreTranslate", "Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector", "postgres_exporter", "WebDataStudio" })]
    [InlineData("Local demo", new[] { "Mailpit", "Ollama", "LibreTranslate", "WebDataStudio" })]
    public void Every_preset_is_a_valid_spec_with_its_containers(string name, string[] containers)
    {
        var preset = Preset.All.Single(p => p.Name == name);
        var spec = new AppSpec
        {
            Name = "Shop",
            Options = preset.Values.ToDictionary(v => v.Key, v => v.Value is "true" or "false" ? JsonValue.Create(v.Value == "true") : (JsonNode?)JsonValue.Create(v.Value)),
        };

        SpecValidator.Validate(spec).ShouldBeEmpty();
        SolutionSummary.For(spec).Containers.ShouldBe(containers);
    }
}
