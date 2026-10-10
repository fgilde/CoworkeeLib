using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void Every_text_of_the_option_catalog_has_a_german_translation()
    {
        var texts = OptionCatalog.All.SelectMany(o => new[] { o.Title, o.Description, o.WhenToUse }
            .Concat(o.Pros).Concat(o.Cons).Concat(o.Choices?.Select(c => c.Description) ?? []))
            .Where(t => t != "None");

        texts.Where(t => !German.Texts.ContainsKey(t)).ShouldBeEmpty();
    }

    [Fact]
    public void Texts_without_a_translation_stay_english() =>
        German.Texts.ContainsKey("Coworkee Studio").ShouldBeFalse();
}
