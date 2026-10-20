using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coworkee.Localization.Tests;

/// <summary>
/// Texts written as L["..."] or T("...") in the source exist in the German file; keys built at runtime are out of reach.
/// UntranslatedKeys.txt lists the library texts that have no German entry yet (the reporting module): it must not grow, and an entry goes once the text exists.
/// </summary>
public sealed partial class LiteralKeysTests
{
    private static readonly string Tests = Path.Combine(ShippedLanguagesTests.RepositoryRoot(), "tests", "Coworkee.Localization.Tests");
    private static readonly string Source = Path.Combine(ShippedLanguagesTests.RepositoryRoot(), "src");

    [Fact]
    public void Literal_keys_of_the_library_exist_in_its_German_file()
    {
        var allowed = File.ReadAllLines(Path.Combine(Tests, "UntranslatedKeys.txt")).Where(l => l.Length > 0).ToList();
        var missing = Missing(Path.Combine("Coworkee.Localization", "Localization", "de.json"), file => !IsFolder(file, "Coworkee.AuthServer"));

        missing.Except(allowed, StringComparer.Ordinal).ShouldBeEmpty("add the text to de.json and every language file");
        allowed.Except(missing, StringComparer.Ordinal).ShouldBeEmpty("these texts exist now: remove them from UntranslatedKeys.txt");
    }

    [Fact]
    public void Literal_keys_of_the_account_pages_exist_in_their_German_file() =>
        Missing(Path.Combine("Coworkee.AuthServer", "Texts", "de.json"), file => IsFolder(file, "Coworkee.AuthServer")).ShouldBeEmpty();

    private static bool IsFolder(string file, string project) => file.StartsWith(Path.Combine(Source, project) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static List<string> Missing(string german, Func<string, bool> files)
    {
        var known = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Source, german)))!.Keys.ToHashSet(StringComparer.Ordinal);
        return
        [
            .. Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cshtml", StringComparison.Ordinal))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(f => !IsFolder(f, "Coworkee.Cli"))
                .Where(files)
                .SelectMany(f => Keys(File.ReadAllText(f)))
                .Where(key => !known.Contains(key))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    // L["..."], l["..."] and T("..."); both texts of L[a ? "..." : "..."]; the texts PageHeader and CoworkeeDataTable translate themselves
    private static IEnumerable<string> Keys(string source) =>
        Literal().Matches(source).Select(m => m.Groups[1].Value)
            .Concat(Choice().Matches(source).SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value }))
            .Concat(Tag().Matches(source).SelectMany(t => Parameter().Matches(t.Value)).Select(m => m.Groups[1].Value))
            .Select(Regex.Unescape);

    [GeneratedRegex("""\b(?:[Ll]\[|T\()"((?:[^"\\]|\\.)*)"(?!\s*\+)""")]
    private static partial Regex Literal();

    [GeneratedRegex("""\b[Ll]\[[^\[\]"\n]*\?\s*"((?:[^"\\]|\\.)*)"\s*:\s*"((?:[^"\\]|\\.)*)"\s*\]""")]
    private static partial Regex Choice();

    [GeneratedRegex("""<(?:PageHeader|CoworkeeDataTable)\b[^>]*>""", RegexOptions.Singleline)]
    private static partial Regex Tag();

    [GeneratedRegex("""\b(?:Title|Description|CreateText|NoRecordsText|SearchPlaceholder)="([^"@]+)\x22""")]
    private static partial Regex Parameter();
}
