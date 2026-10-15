namespace Coworkee.Mailing.Tests;

public sealed class MailCulturesTests
{
    [Theory]
    [InlineData(null, "en")]
    [InlineData("de-AT", "de-AT,de,en")]
    [InlineData("zh-CN", "zh-CN,zh,en")]
    [InlineData("zh-Hant", "zh-Hant,en")]
    [InlineData("zh-TW", "zh-TW,en")]
    [InlineData("en-GB", "en-GB,en")]
    public void Traditional_Chinese_does_not_use_Simplified_templates(string? culture, string candidates)
    {
        MailCultures.Candidates(culture).ShouldBe(candidates.Split(','));
    }

    [Fact]
    public void The_default_language_comes_before_English()
    {
        MailCultures.Candidates("fr-CA", "de-AT").ShouldBe(["fr-CA", "fr", "de-AT", "de", "en"]);
    }
}
