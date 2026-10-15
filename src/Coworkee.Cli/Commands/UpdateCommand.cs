using System.CommandLine;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Coworkee.Cli.Apps;

namespace Coworkee.Cli.Commands;

internal static partial class UpdateCommand
{
    private const string Index = "https://api.nuget.org/v3-flatcontainer/coworkee.core/index.json";

    private static readonly Option<string?> Version = new("--version") { Description = "Coworkee version to use (default: the newest on nuget.org)." };
    private static readonly Option<bool> Prerelease = new("--prerelease") { Description = "Allow prerelease versions when looking up the newest." };
    private static readonly Option<bool> DryRun = new("--dry-run") { Description = "Show the breaking changes and what would change, change nothing." };

    public static Command Create() => new Command("update", "Set the Coworkee package version of the solution, apply the upgrade rules of the versions in between and list the manual steps.") { Version, Prerelease, DryRun }
        .Handle(RunAsync);

    public static string Latest(IEnumerable<string> versions, bool prerelease) =>
        versions.LastOrDefault(v => prerelease || !v.Contains('-')) ?? throw new CliException("No Coworkee version found on nuget.org.");

    public static string SetVersion(string packagesProps, string version) =>
        CoworkeeVersion().IsMatch(packagesProps)
            ? CoworkeeVersion().Replace(packagesProps, $"<CoworkeeVersion>{version}</CoworkeeVersion>", 1)
            : throw new CliException("Directory.Packages.props has no <CoworkeeVersion>.");

    /// <summary>Moves the app in <paramref name="root"/> from its version to <paramref name="version"/>: version, rules, manual steps.</summary>
    public static int Upgrade(string root, string version, bool dryRun)
    {
        var props = Path.Combine(root, "Directory.Packages.props");
        var from = OptionApplier.Version(root);
        var rules = UpgradeRules.Between(from, version);
        foreach (var rule in rules.Where(r => r.Breaking is { Length: > 0 }))
        {
            Console.WriteLine($"Breaking changes in {rule.Version}:");
            foreach (var line in rule.Breaking!)
            {
                Console.WriteLine($"  - {line}");
            }
        }

        if (!dryRun)
        {
            File.WriteAllText(props, SetVersion(File.ReadAllText(props), version));
        }

        Console.WriteLine($"{(dryRun ? "Would set" : "Set")} Coworkee {from ?? "?"} -> {version} in Directory.Packages.props.");
        foreach (var change in UpgradeRules.Apply(root, rules, dryRun))
        {
            Console.WriteLine($"{(dryRun ? "would change" : "changed")} {change}");
        }

        var manual = rules.SelectMany(r => r.Manual ?? []).Select(m => (m.Text, Hits: UpgradeRules.Manual(root, m))).Where(m => m.Hits.Count > 0).ToList();
        if (manual.Count > 0)
        {
            Console.WriteLine("Manual steps:");
            foreach (var (text, hits) in manual)
            {
                Console.WriteLine($"  - {text}");
                foreach (var hit in hits)
                {
                    Console.WriteLine($"      {hit}");
                }
            }
        }

        Console.WriteLine(rules.Any(r => r.NeedsMigration)
            ? "Run dotnet restore, then coworkee migrations add CoworkeeUpdate: the model of the library changed."
            : "Run dotnet restore; add a migration (coworkee migrations add CoworkeeUpdate) if your modules changed their model.");
        return 0;
    }

    private static async Task<int> RunAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var root = Solution.Find(Environment.CurrentDirectory).Root;
        var version = result.GetValue(Version) ?? Latest(await FetchVersionsAsync(cancellationToken), result.GetValue(Prerelease));
        return Upgrade(root, version, result.GetValue(DryRun));
    }

    private static async Task<string[]> FetchVersionsAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        try
        {
            return (await http.GetFromJsonAsync<VersionIndex>(Index, cancellationToken))!.Versions;
        }
        catch (HttpRequestException exception)
        {
            throw new CliException($"Could not read the Coworkee versions from nuget.org ({exception.Message}). Pass --version.");
        }
    }

    [GeneratedRegex("<CoworkeeVersion>[^<]*</CoworkeeVersion>")]
    private static partial Regex CoworkeeVersion();

    private sealed record VersionIndex(string[] Versions);
}
