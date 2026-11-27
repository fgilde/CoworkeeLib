using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coworkee.ArchitectureTests;

/// <summary>
/// The library's texts say tenant (German Mandant) and group, never organisation: in an app built on it, organisations are business entities of its own.
/// The scan covers what a person reads: the keys and the English and German values of the localization files, texts in <c>L["..."]</c>, in
/// <c>Title</c>, <c>Description</c>, <c>Label</c>, <c>Text</c>, <c>HelperText</c> and <c>Placeholder</c> attributes, the messages of <c>Error.*</c> and the
/// display names of permissions. Identifiers, comments, log messages and exception messages are not texts and stay out.
/// </summary>
public sealed partial class UiWordingTests
{
    [GeneratedRegex("rgani[sz]ation", RegexOptions.IgnoreCase)]
    private static partial Regex Organisation();

    private const string Text = "\"((?:[^\"\\\\]|\\\\.)*)\"";

    private static readonly Regex[] UiLiterals =
    [
        new($@"\bl\[\s*{Text}", RegexOptions.IgnoreCase),
        new($@"\b(?:Title|Description|Label|Text|HelperText|Placeholder)\s*[=:]\s*{Text}"),
        new($@"\bError\.\w+\([^;]*?,\s*{Text}\s*\)"),
    ];

    private static readonly Regex PermissionName = new($@"\.(?:Add|Group)\(.*?,\s*{Text}");

    [Fact]
    public void No_library_ui_text_says_organisation()
    {
        var hits = new List<string>();
        var root = Root();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Coworkee.Localization", "Localization"), "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var language = Path.GetFileNameWithoutExtension(file);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (Organisation().IsMatch(entry.Name)) hits.Add($"{language}.json key: {entry.Name}");
                if (language == "de" && Organisation().IsMatch(entry.Value.GetString() ?? "")) hits.Add($"de.json value: {entry.Value.GetString()}");
            }
        }

        foreach (var file in SourceFiles(root))
            foreach (var text in UiTexts(Path.GetFileName(file), File.ReadAllLines(file)).Where(t => Organisation().IsMatch(t)))
                hits.Add($"{Path.GetRelativePath(root, file)}: {text}");

        hits.ShouldBeEmpty("say tenant or group; the texts found:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void The_scan_finds_ui_texts_and_ignores_the_rest()
    {
        string[] lines =
        [
            "<PageHeader Title=\"Users\" Description=\"Accounts of this organisation.\" />",
            "var a = L[\"Set as organisation dashboard\"];",
            "Error.Forbidden(\"x.y\", \"Managed from the system organisation.\");",
            "// L[\"a comment about the organisation\"]",
            "[LoggerMessage(Message = \"Job work for organisation {TenantId} failed\")]",
            "throw new InvalidOperationException(\"Reports belong to an organisation.\");",
            "var organisation = tenants.Find(\"Organisation\");",
        ];

        UiTexts("X.cs", lines).ShouldBe(["Users", "Accounts of this organisation.", "Set as organisation dashboard", "Managed from the system organisation."]);
    }

    private static IEnumerable<string> UiTexts(string fileName, IEnumerable<string> lines)
    {
        var permissions = fileName.EndsWith("PermissionDefinitions.cs", StringComparison.Ordinal);
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) || line.TrimStart().StartsWith("@*", StringComparison.Ordinal)) continue;
            foreach (var pattern in permissions ? [.. UiLiterals, PermissionName] : UiLiterals)
                foreach (Match match in pattern.Matches(line))
                    yield return match.Groups[1].Value;
        }
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => !Regex.IsMatch(f, @"[\\/](obj|bin|Coworkee\.Studio|Coworkee\.Cli\.Spec)[\\/]"));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Coworkee.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Coworkee.slnx not found above the test output.");
    }
}
