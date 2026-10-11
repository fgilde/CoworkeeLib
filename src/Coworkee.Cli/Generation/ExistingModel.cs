using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>What an app has already, read from its files: entities with an OData set (generated and sample ones) and generated enums.</summary>
internal static partial class ExistingModel
{
    public static SpecIndex Index(Solution solution, AppSpec spec) => new(spec, Entities(solution), Enums(solution));

    public static IReadOnlyList<KnownEntity> Entities(Solution solution) =>
    [
        .. Directory.EnumerateDirectories(solution.Src, solution.Name + ".*")
            .SelectMany(project => Directory.EnumerateFiles(project, "*.cs").SelectMany(file => ODataSet().Matches(File.ReadAllText(file)))
                .Select(match => Entity(project, Path.GetFileName(project)[(solution.Name.Length + 1)..], match.Groups[1].Value, match.Groups[2].Value))),
    ];

    public static IReadOnlyList<string> Enums(Solution solution)
    {
        var folder = Path.Combine(solution.Src, solution.Name + ".Contracts", "Enums");
        return Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "*.cs").Select(Path.GetFileNameWithoutExtension).OfType<string>()] : [];
    }

    // the display of an existing entity: its first non-nullable string
    private static KnownEntity Entity(string project, string module, string name, string plural)
    {
        var domain = Path.Combine(project, "Domain", name + ".cs");
        var display = File.Exists(domain) && Text().Match(File.ReadAllText(domain)) is { Success: true } text ? text.Groups[1].Value : null;
        return new KnownEntity(module, name, plural, display);
    }

    [GeneratedRegex(@"AddODataEntity<(\w+)>\(""(\w+)""")]
    private static partial Regex ODataSet();

    [GeneratedRegex(@"public (?:required )?string (\w+) \{ get; set; \}")]
    private static partial Regex Text();
}
