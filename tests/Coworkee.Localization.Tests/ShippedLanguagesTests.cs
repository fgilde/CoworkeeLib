using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;

namespace Coworkee.Localization.Tests;

/// <summary>Every shipped language has all texts of the German files: library UI, account pages, the template app and the mails.</summary>
public sealed partial class ShippedLanguagesTests
{
    private static readonly string Root = RepositoryRoot();

    private static readonly string[] TextFolders =
    [
        Path.Combine("src", "Coworkee.Localization", "Localization"),
        Path.Combine("src", "Coworkee.AuthServer", "Texts"),
        Path.Combine("templates", "Coworkee.Templates", "content", "CoworkeeApp", "src", "MyApp.Contracts", "Localization"),
    ];

    private static readonly string MailFolder = Path.Combine(Root, "src", "Coworkee.Mailing", "Templates");

    private static IEnumerable<string> Languages => OptionCatalog.ShippedLanguages.Where(l => l != "en");

    public static TheoryData<string, string> Texts => [.. TextFolders.SelectMany(folder => Languages.Select(language => (folder, language)))];

    public static TheoryData<string> MailLanguages => [.. Languages];

    [Fact]
    public void The_library_default_languages_are_the_ones_the_templates_start_with()
    {
        LocalizationOptions.Default.ShouldBe(OptionCatalog.DefaultLanguages);
    }

    [Fact]
    public void Texts_and_docs_name_23_shipped_languages_so_a_change_updates_them()
    {
        OptionCatalog.ShippedLanguages.Count.ShouldBe(23, "update the \"23 languages\" in OptionCatalog, LanguagesStep, German.cs of the Studio and the website docs");
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void Texts_have_the_keys_of_the_German_file_and_keep_their_placeholders(string folder, string language)
    {
        var path = TextFile(folder, language);
        File.Exists(path).ShouldBeTrue($"{path} is missing");

        var german = Read(TextFile(folder, "de"));
        var texts = Read(path);
        var wrong = texts.Where(t => !Tokens(Placeholder(), Source(german, t.Key)).SequenceEqual(Tokens(Placeholder(), t.Value))).Select(t => t.Key);

        german.Keys.Except(texts.Keys, StringComparer.Ordinal).ShouldBeEmpty($"{language}: missing keys");
        texts.Keys.Except(german.Keys, StringComparer.Ordinal).ShouldBeEmpty($"{language}: keys the German file lacks");
        wrong.ShouldBeEmpty($"{language}: placeholders differ from the key");
    }

    // MudBlazor.Extensions localizes its own English texts through the app's texts; unshipped ones get machine translated ("Drop Dateien hier")
    [Fact]
    public void The_library_ships_the_texts_of_the_MudBlazor_Extensions_components_it_shows()
    {
        var german = Read(TextFile(TextFolders[0], "de"));

        new[]
        {
            "Drop files here", "Drop/Paste files or folders here", "Drop/Paste files here", "Drop/Paste folders here", "Drop/Paste is not allowed", "Upload Files",
            "A maximum of {0} files are allowed", "No declarations yet. Click + to add one.", "Add property", "Filter properties...", "Preview text — Aa Bb Cc 123",
            "Categorized", "Repeats", "Every few minutes", "Every day at {0} UTC", "This folder is empty.", "Select a file to preview it.",
        }.Where(k => !german.ContainsKey(k)).ShouldBeEmpty();
        german["Drop files here"].ShouldBe("Dateien hier ablegen");
    }

    [Theory]
    [MemberData(nameof(MailLanguages))]
    public void Mail_templates_exist_for_every_German_one_with_its_expressions_and_tags(string language)
    {
        var problems = new List<string>();
        foreach (var german in Directory.GetFiles(MailFolder, "*.de.*.scriban"))
        {
            var name = Path.GetFileName(german);
            var path = Path.Combine(MailFolder, name.Replace(".de.", $".{language}.", StringComparison.Ordinal));
            if (!File.Exists(path))
            {
                problems.Add($"{Path.GetFileName(path)} is missing");
            }
            else if (!Tokens(Markup(), File.ReadAllText(german)).SequenceEqual(Tokens(Markup(), File.ReadAllText(path))))
            {
                problems.Add($"{Path.GetFileName(path)}: expressions or tags differ from {name}");
            }
        }

        problems.ShouldBeEmpty();
    }

    // keys that are no English sentence (MudBlazor ones such as MudDataGridPager_InfoFormat) take their placeholders from the German text
    private static string Source(Dictionary<string, string> german, string key) => key.Contains(' ') ? key : german.GetValueOrDefault(key, key);

    private static string TextFile(string folder, string language) => Path.Combine(Root, folder, $"{language}.json");

    private static Dictionary<string, string> Read(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];

    private static List<string> Tokens(Regex token, string text) =>
        [.. token.Matches(text).Select(m => HtmlLang().Replace(Whitespace().Replace(m.Value, " "), "lang=\"\"")).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\d[^{}]*\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{\{.*?\}\}|</?[A-Za-z][^>]*>", RegexOptions.Singleline)]
    private static partial Regex Markup();

    // the lang value differs per language; right-to-left languages add dir="rtl"
    [GeneratedRegex("\\blang=\"[^\"]*\"( dir=\"[^\"]*\")?")]
    private static partial Regex HtmlLang();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Coworkee.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Coworkee.slnx not found above the test output.");
    }
}
