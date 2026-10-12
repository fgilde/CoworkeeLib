namespace Coworkee.Studio.Tests;

public sealed class LanguagesStepTests
{
    [Fact]
    public void Choosing_a_language_keeps_the_shipped_ones_and_search_finds_it_by_any_name()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        var step = main.Languages;

        step.Chosen.Select(l => l.Code).ShouldBe(["en", "de"]);
        step.Search = "franç";
        step.VisibleLanguages.Select(l => l.Code).ShouldBe(["fr"]);
        step.Search = "Japanese";
        step.VisibleLanguages.Single().Code.ShouldBe("ja");

        step.Languages.Single(l => l.Code == "fr").IsChecked = true;
        main.Option("languages").Value.ShouldBe("en,de,fr");
        step.Default = step.Chosen.Single(l => l.Code == "fr");
        main.Option("defaultLanguage").Value.ShouldBe("fr");
        step.ChosenText.ShouldBe("3 languages, 1 machine translated");
        main.Errors.ShouldBeEmpty();
        main.Steps.ToList().IndexOf(step).ShouldBe(main.Steps.ToList().IndexOf(main.Architecture) + 1);
    }

    [Fact]
    public void Services_are_cards_with_their_costs_and_errors_land_on_the_page()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        var step = main.Languages;

        step.Services.Single(s => s.IsSelected).Value.ShouldBe("libreTranslate");
        step.ServiceNoteText.ShouldContain("LibreTranslate");
        main.Architecture.Summary.Containers.ShouldContain("LibreTranslate");

        step.SelectServiceCommand.Execute(step.Services.Single(s => s.Value == "deepL"));
        main.Option("translationProvider").Value.ShouldBe("deepL");
        step.ServiceNoteText.ShouldContain("Parameters:translation-key");
        main.Architecture.Summary.Containers.ShouldNotContain("LibreTranslate");
        step.Services.Single(s => s.Value == "myMemory").Cons.ShouldContain(c => c.Contains("5,000"));

        step.Languages.Single(l => l.Code == "es").IsChecked = true;
        step.SelectServiceCommand.Execute(step.Services.Single(s => s.Value == "none"));
        step.Errors.ShouldContain(e => e.StartsWith("translationProvider:", StringComparison.Ordinal));
        main.Architecture.Groups.SelectMany(g => g.Options).Select(o => o.Id).ShouldNotContain("translationProvider");
    }
}
