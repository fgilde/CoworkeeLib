using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coworkee.ArchitectureTests;

/// <summary>
/// The library's texts say tenant (German Mandant) and group, never organisation: in an app built on it, organisations are business entities of its own.
/// The scan covers what a person reads: the keys and the English and German values of the localization files; texts in <c>L["..."]</c>, in
/// <c>Title</c>, <c>Description</c>, <c>Label</c>, <c>Text</c>, <c>HelperText</c>, <c>Placeholder</c>, <c>Message</c> and <c>Display(Name = ...)</c>,
/// <c>Snackbar.Add</c>, <c>Result.Failure</c>, the messages of <c>Error.*</c> and the display names of permissions (also when a statement or a literal
/// spans lines); every literal of the CLI option catalog and the Studio translations; the CLI upgrade rules and the published spec schema.
/// Identifiers, comments, log messages and exception messages are not texts and stay out.
/// </summary>
public sealed partial class UiWordingTests
{
    [GeneratedRegex("rgani[sz]ation", RegexOptions.IgnoreCase)]
    private static partial Regex Organisation();

    private const string Literal = "\"((?:[^\"\\\\]|\\\\.)*)\"";

    private static readonly Regex Tokens = new($"{Literal}|//[^\\n]*|/\\*.*?\\*/|@\\*.*?\\*@", RegexOptions.Singleline);
    private static readonly Regex Joined = new("\"\\s*\\+\\s*\\$?\"");
    private static readonly Regex LoggerMessage = new(@"\[LoggerMessage\([^\]]*\]", RegexOptions.Singleline);

    private static readonly Regex[] UiLiterals =
    [
        new($@"\bl\[\s*\$?{Literal}", RegexOptions.IgnoreCase),
        new($@"\b(?:Title|Description|Label|Text|HelperText|Placeholder|Message|ToolTip|Tooltip|Header)\s*[=:]\s*\$?{Literal}"),
        new($@"\b(?:Description|DisplayName)\(\s*\$?{Literal}"),
        new($@"\bDisplay\([^)]*?\bName\s*=\s*\$?{Literal}"),
        new($@"\bError\.\w+\([^;]*?,\s*\$?{Literal}\s*[,)]"),
        new($@"\bResult(?:<\w+>)?\.(?:Failure|Fail)\(\s*\$?{Literal}"),
        new($@"\bSnackbar\.Add\(\s*\$?{Literal}"),
    ];

    private static readonly Regex PermissionName = new($@"\.(?:Add|Group)\([^;]*?,\s*{Literal}");
    private static readonly Regex AnyLiteral = new(Literal);

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

        foreach (var file in new[] { Path.Combine(root, "src", "Coworkee.Cli", "upgrade-rules.json"), Path.Combine(root, "website", "docs", "assets", "spec.schema.json") })
            foreach (var text in AnyLiteral.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value).Where(t => Organisation().IsMatch(t)))
                hits.Add($"{Path.GetRelativePath(root, file)}: {text}");

        foreach (var file in SourceFiles(root))
        {
            var everyLiteral = Regex.IsMatch(file, @"[\\/]Coworkee\.Studio[\\/]Localization[\\/]") || file.EndsWith("OptionCatalog.cs", StringComparison.Ordinal);
            foreach (var text in UiTexts(Path.GetFileName(file), File.ReadAllText(file), everyLiteral).Where(t => Organisation().IsMatch(t)))
                hits.Add($"{Path.GetRelativePath(root, file)}: {text}");
        }

        hits.ShouldBeEmpty("say tenant or group; the texts found:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void The_scan_finds_ui_texts_and_ignores_the_rest()
    {
        const string source = """
            <PageHeader Title="Users" Description="Accounts of this organisation." />
            var a = L["Set as organisation dashboard"];
            Error.Forbidden("x.y", "Managed from the system organisation.");
            Snackbar.Add("Saved for every organisation", Severity.Success);
            [Description("Lists the people of the organisation")]
            [Display(Name = "Organisation name", Order = 1)]
            return Result.Failure("Unknown organisation");
            public const string Message = "Mail of an organisation";
            return Error.Validation(
                nameof(Request.Name),
                "A name that spans " +
                "lines of the organisation.");
            // L["a comment about the organisation"]
            /* Snackbar.Add("a block comment about the organisation") */
            @* <PageHeader Title="a razor comment about the organisation" /> *@
            [LoggerMessage(Level = LogLevel.Error, Message = "Job work for organisation {TenantId} failed")]
            throw new InvalidOperationException("Reports belong to an organisation.");
            var organisation = tenants.Find("Organisation");
            var url = "http://example.test/organisation";
            """;

        UiTexts("X.cs", source, everyLiteral: false).OrderBy(t => t, StringComparer.Ordinal).ShouldBe(
            new[]
            {
                "Users", "Accounts of this organisation.", "Set as organisation dashboard", "Managed from the system organisation.",
                "Saved for every organisation", "Lists the people of the organisation", "Organisation name", "Unknown organisation",
                "Mail of an organisation", "A name that spans lines of the organisation.",
            }.OrderBy(t => t, StringComparer.Ordinal));

        UiTexts("X.cs", "new(\"files\", \"Files\", \"Folders of the organisation.\");", everyLiteral: true).ShouldContain("Folders of the organisation.");
        UiTexts("XPermissionDefinitions.cs", "context.Group(\"g\", \"G\").Add(P.A,\n    \"Write for the organisation\");", everyLiteral: false).ShouldContain("Write for the organisation");
    }

    private static IEnumerable<string> UiTexts(string fileName, string text, bool everyLiteral)
    {
        // comments out (strings kept: a "//" inside a literal is not a comment), log attributes out, literals split with + joined
        text = Tokens.Replace(text, m => m.Value.StartsWith('"') ? m.Value : "");
        text = LoggerMessage.Replace(text, "");
        for (var joined = Joined.Replace(text, ""); joined != text; joined = Joined.Replace(text, "")) text = joined;

        if (everyLiteral)
            return AnyLiteral.Matches(text).Select(m => m.Groups[1].Value);

        var patterns = fileName.EndsWith("PermissionDefinitions.cs", StringComparison.Ordinal) ? [.. UiLiterals, PermissionName] : UiLiterals;
        return patterns.SelectMany(p => p.Matches(text).Select(m => m.Groups[1].Value)).ToList();
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => !Regex.IsMatch(f, @"[\\/](obj|bin)[\\/]"));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Coworkee.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Coworkee.slnx not found above the test output.");
    }
}
