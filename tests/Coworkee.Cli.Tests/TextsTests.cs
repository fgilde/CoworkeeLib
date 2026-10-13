using System.Text.Json;
using Coworkee.Cli;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class TextsTests
{
    private static string Lib(string language) => Path.Combine(Dotnet.RepositoryRoot(), "src", "Coworkee.Localization", "Localization", language + ".json");

    private static Dictionary<string, string> Read(string path) => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

    private static string[] Keys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.EnumerateObject().Select(p => p.Name)];
    }

    [Fact]
    public void Export_writes_the_library_texts_of_the_language()
    {
        using var folder = new TempFolder();
        var output = Path.Combine(folder.Path, "fr.json");

        var exit = CliApp.Build().Parse(["texts", "export", "--lang", "fr", "--out", output]).Invoke();

        exit.ShouldBe(0);
        Read(output).ShouldBe(Read(Lib("fr")));
        Keys(output).ShouldBe(Keys(Lib("fr")));
        File.ReadAllText(output).ShouldContain("é");
    }

    [Fact]
    public void Export_of_english_uses_the_keys_as_texts()
    {
        using var folder = new TempFolder();
        var output = Path.Combine(folder.Path, "en.json");

        TextsCommand.Export(output, "en", keys: null);

        Read(output).ShouldAllBe(p => p.Key == p.Value);
        Keys(output).ShouldBe(Keys(Lib("de")));
    }

    [Fact]
    public void Export_keeps_existing_texts_and_adds_the_missing_keys()
    {
        using var folder = new TempFolder();
        var output = Path.Combine(folder.Path, "de.json");
        File.WriteAllText(output, "{\r\n  \"Zebra\": \"Zebra\",\r\n  \"Home\": \"Mein Start\"\r\n}\r\n");

        TextsCommand.Export(output, "de", keys: null);

        var keys = Keys(output);
        var texts = Read(output);
        keys.Take(2).ShouldBe(["Zebra", "Home"]);
        texts["Home"].ShouldBe("Mein Start");
        texts["Filter"].ShouldBe(Read(Lib("de"))["Filter"]);
        keys.ShouldBe(["Zebra", "Home", .. Keys(Lib("de")).Where(k => k != "Home")]);
    }

    [Fact]
    public void Export_filters_the_keys_by_glob_or_regex()
    {
        using var folder = new TempFolder();
        var glob = Path.Combine(folder.Path, "glob.json");
        var regex = Path.Combine(folder.Path, "regex.json");

        TextsCommand.Export(glob, "de", "Home");
        TextsCommand.Export(regex, "de", "/^(Home|Filter)$/");
        TextsCommand.Export(Path.Combine(folder.Path, "all.json"), "de", "*");

        Keys(glob).ShouldBe(["Home"]);
        Keys(regex).ShouldBe(["Home", "Filter"]);
        Keys(Path.Combine(folder.Path, "all.json")).ShouldBe(Keys(Lib("de")));
        Keys(Lib("de")).Count(k => TextsCommand.Matches("Ex*", k)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Export_of_an_unknown_language_lists_the_shipped_ones()
    {
        using var folder = new TempFolder();

        var exception = Should.Throw<CliException>(() => TextsCommand.Export(Path.Combine(folder.Path, "xx.json"), "xx", keys: null));

        exception.Message.ShouldContain("xx");
        OptionCatalog.ShippedLanguages.ShouldAllBe(l => exception.Message.Contains(l));
        File.Exists(Path.Combine(folder.Path, "xx.json")).ShouldBeFalse();
    }

    [Fact]
    public void Export_without_out_writes_into_the_contracts_of_the_app()
    {
        using var folder = new TempFolder();
        folder.File("Shop.slnx");
        folder.File("src/Shop.Contracts/Shop.Contracts.csproj");

        TextsCommand.DefaultOutput(Solution.Find(folder.Path), "fr").ShouldBe(Path.Combine(folder.Path, "src", "Shop.Contracts", "Localization", "fr.json"));
    }

    [Fact]
    public void Export_without_out_needs_the_contracts_project()
    {
        using var folder = new TempFolder();
        folder.File("Shop.slnx");
        Directory.CreateDirectory(Path.Combine(folder.Path, "src"));

        Should.Throw<CliException>(() => TextsCommand.DefaultOutput(Solution.Find(folder.Path), "fr")).Message.ShouldContain("Contracts.csproj");
    }

    [Fact]
    public void Export_counts_the_new_texts_and_the_ones_already_there()
    {
        using var folder = new TempFolder();
        var output = Path.Combine(folder.Path, "de.json");
        File.WriteAllText(output, "{\r\n  \"Home\": \"Mein Start\"\r\n}\r\n");

        TextsCommand.Export(output, "de", "/^(Home|Filter)$/").ShouldBe((1, 1));
        TextsCommand.Export(output, "de", "/^(Home|Filter)$/").ShouldBe((0, 2));
    }

    [Fact]
    public void Export_rejects_a_broken_regex_and_a_language_that_is_no_code()
    {
        using var folder = new TempFolder();

        Should.Throw<CliException>(() => TextsCommand.Export(Path.Combine(folder.Path, "a.json"), "de", "/(/"));
        Should.Throw<CliException>(() => TextsCommand.Export(Path.Combine(folder.Path, "b.json"), "../de", keys: null));
    }

    [Fact]
    public void Export_reports_a_pattern_that_takes_too_long()
    {
        using var folder = new TempFolder();

        Should.Throw<CliException>(() => TextsCommand.Export(Path.Combine(folder.Path, "a.json"), "de", "/^(\\w+\\s?)*\\d$/")).Message.ShouldContain("too long");
        File.Exists(Path.Combine(folder.Path, "a.json")).ShouldBeFalse();
    }
}
