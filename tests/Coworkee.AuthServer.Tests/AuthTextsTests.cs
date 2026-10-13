using System.Globalization;

namespace Coworkee.AuthServer.Tests;

public sealed class AuthTextsTests
{
    [Theory]
    [InlineData("zh")]
    [InlineData("zh-CN")]
    [InlineData("zh-Hans")]
    public void Simplified_Chinese_gets_the_zh_texts(string culture)
    {
        AuthTexts.Find("Account", CultureInfo.GetCultureInfo(culture)).ShouldBe(AuthTexts.Find("Account", CultureInfo.GetCultureInfo("zh")));
        AuthTexts.Find("Account", CultureInfo.GetCultureInfo(culture)).ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("zh-Hant")]
    [InlineData("zh-TW")]
    [InlineData("zh-HK")]
    public void Traditional_Chinese_gets_no_Simplified_texts(string culture)
    {
        AuthTexts.Find("Account", CultureInfo.GetCultureInfo(culture)).ShouldBeNull();
    }

    [Fact]
    public void A_region_falls_back_to_its_language()
    {
        AuthTexts.Find("Account", CultureInfo.GetCultureInfo("de-AT")).ShouldBe("Konto");
    }
}
