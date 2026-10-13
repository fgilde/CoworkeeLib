using Coworkee.Contracts.Localization;
using Coworkee.Localization.Resources;

namespace Coworkee.Localization.Tests;

public sealed class ShippedCulturesTests
{
    [Theory]
    [InlineData("de", "de")]
    [InlineData("de-AT", "de-AT,de")]
    [InlineData("zh", "zh")]
    [InlineData("zh-CN", "zh-CN,zh")]
    [InlineData("zh-Hans", "zh-Hans,zh")]
    [InlineData("zh-SG", "zh-SG,zh")]
    [InlineData("zh-Hant", "zh-Hant")]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("zh-HK", "zh-HK")]
    [InlineData("zh-Hant-TW", "zh-Hant-TW")]
    public void Traditional_Chinese_never_reads_the_Simplified_file(string culture, string files)
    {
        ShippedCultures.Files(culture).ShouldBe(files.Split(','));
    }

    [Theory]
    [InlineData("zh-CN", "简体")]
    [InlineData("zh", "简体")]
    [InlineData("zh-Hant", null)]
    [InlineData("zh-TW", null)]
    [InlineData("zh-HK", null)]
    public void Resources_of_Traditional_Chinese_are_left_to_the_machine_translation(string culture, string? expected)
    {
        var resources = new LocalizationResources([new Chinese()]);

        resources.For(culture).GetValueOrDefault("Save").ShouldBe(expected);
    }

    private sealed class Chinese : ILocalizationResourceContributor
    {
        public void Define(LocalizationResourceContext context) => context.Add("zh", new Dictionary<string, string> { ["Save"] = "简体" });
    }
}
