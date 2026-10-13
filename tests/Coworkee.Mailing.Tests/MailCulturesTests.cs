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
}
