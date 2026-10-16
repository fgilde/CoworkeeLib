using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed partial class LocalizationTests
{
    [Fact]
    public void Every_text_of_the_option_catalog_has_a_german_translation()
    {
        var texts = OptionCatalog.All.SelectMany(o => new[] { o.Title, o.Description, o.WhenToUse }
            .Concat(o.Pros).Concat(o.Cons).Concat(OptionCatalog.LanguageOptions.Contains(o.Id) ? [] : o.Choices?.Select(c => c.Description) ?? []))
            .Where(t => t != "None");

        texts.Where(t => !German.Texts.ContainsKey(t)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_text_of_the_views_and_view_models_has_a_german_translation()
    {
        var source = Path.GetFullPath(Path.Combine(Here(), "..", "..", "..", "src", "Coworkee.Studio"));
        var texts = Directory.EnumerateFiles(source, "*.axaml", SearchOption.AllDirectories).SelectMany(f => Markup().Matches(File.ReadAllText(f)))
            .Concat(Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories).SelectMany(f => Code().Matches(File.ReadAllText(f))))
            .Select(m => m.Groups["text"].Value)
            .Concat(Preset.All.SelectMany(p => new[] { p.Name, p.Description }))
            .Concat(PropertyViewModel.SearchModes).Concat(EntityViewModel.Mappings.Where(m => m != "Mapperly"))
            .Concat(EntitiesStep.ViewModes).Concat(EntityImportViewModel.Kinds.Take(1)).Concat(EntityImportViewModel.Providers.Take(1));

        texts.Distinct().Where(t => !German.Texts.ContainsKey(t)).ShouldBeEmpty();
    }

    [Fact]
    public Task Every_infrastructure_note_has_a_german_translation() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var notes = new HashSet<string>();
        foreach (var database in new[] { "postgres", "sqlServer", "sqlite" })
        {
            foreach (var provider in OptionCatalog.Find("aiProvider")!.Choices!.Select(c => c.Value))
            {
                foreach (var on in new[] { "true", "false" })
                {
                    main.Option("database").Value = database;
                    main.Option("aiProvider").Value = provider;
                    main.Option("ai").Value = on;
                    main.Option("redis").Value = on;
                    notes.UnionWith(main.Options.Values.Select(o => o.Note).OfType<string>());
                }
            }
        }

        notes.Where(n => !German.Texts.ContainsKey(n)).ShouldBeEmpty();
    });

    [Fact]
    public void Texts_without_a_translation_stay_english() =>
        German.Texts.ContainsKey("Coworkee Studio").ShouldBeFalse();

    [Fact]
    public void German_texts_say_du() =>
        German.Texts.Values.Where(t => Formal().IsMatch(t)).ShouldBeEmpty();

    private static string Here([CallerFilePath] string path = "") => path;

    // {l:L 'Some text'} and {l:L Word}
    [GeneratedRegex(@"\{l:L (?:'(?<text>[^']+)'|(?<text>[^}']+))\}")]
    private static partial Regex Markup();

    // L.Get("..."), L.Format("...") and the texts of the steps
    [GeneratedRegex(@"(?:L\.(?:Get|Format)\(|(?:TitleText|SubtitleText|PitchText) => )""(?<text>[^""]*)""")]
    private static partial Regex Code();

    // Sie/Ihr inside a sentence is the formal address; at a sentence start it may mean "it"/"its"
    [GeneratedRegex(@"(?<![.!?]) (?:Sie|Ihnen|Ihre?[mnrs]?)\b")]
    private static partial Regex Formal();
}
