using System.CommandLine;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Coworkee.Cli.Commands;

internal static partial class UpdateCommand
{
    private const string Index = "https://api.nuget.org/v3-flatcontainer/coworkee.core/index.json";

    private static readonly Option<string?> Version = new("--version") { Description = "Coworkee version to use (default: the newest on nuget.org)." };
    private static readonly Option<bool> Prerelease = new("--prerelease") { Description = "Allow prerelease versions when looking up the newest." };

    public static Command Create() => new Command("update", "Set the Coworkee package version of the solution.") { Version, Prerelease }
        .Handle(RunAsync);

    public static string Latest(IEnumerable<string> versions, bool prerelease) =>
        versions.LastOrDefault(v => prerelease || !v.Contains('-')) ?? throw new CliException("No Coworkee version found on nuget.org.");

    public static string SetVersion(string packagesProps, string version) =>
        CoworkeeVersion().IsMatch(packagesProps)
            ? CoworkeeVersion().Replace(packagesProps, $"<CoworkeeVersion>{version}</CoworkeeVersion>", 1)
            : throw new CliException("Directory.Packages.props has no <CoworkeeVersion>.");

    private static async Task<int> RunAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var props = Path.Combine(Solution.Find(Environment.CurrentDirectory).Root, "Directory.Packages.props");
        var version = result.GetValue(Version) ?? Latest(await FetchVersionsAsync(cancellationToken), result.GetValue(Prerelease));
        await File.WriteAllTextAsync(props, SetVersion(await File.ReadAllTextAsync(props, cancellationToken), version), cancellationToken);
        Console.WriteLine($"Coworkee {version} set in {props}. Run dotnet restore, then coworkee migrations add CoworkeeUpdate if the modules changed their model.");
        return 0;
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
