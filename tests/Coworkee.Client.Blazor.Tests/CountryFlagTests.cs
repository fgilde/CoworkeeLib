using System.Globalization;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CountryFlagTests : ClientTestBase
{
    [Theory]
    [InlineData("DE", "DE")]
    [InlineData(" de ", "DE")]
    [InlineData("Germany", "DE")]
    [InlineData("germany", "DE")]
    [InlineData("Deutschland", "DE")]
    [InlineData("DEU", "DE")]
    [InlineData("United States", "US")]
    [InlineData("xk", "XK")]
    [InlineData("ZZ", null)]
    [InlineData("Narnia", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Codes_and_names_resolve_to_the_two_letter_code(string? value, string? expected) =>
        CountryCodes.Resolve(value).ShouldBe(expected);

    [Fact]
    public void Names_in_the_current_ui_language_resolve()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");
        try
        {
            CountryCodes.Resolve(new RegionInfo("DE").DisplayName).ShouldBe("DE");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("de-DE", "DE")]
    [InlineData("de", "DE")]
    [InlineData("de-AT", "AT")]
    [InlineData("en", "GB")]
    [InlineData("en-US", "US")]
    [InlineData("ar", "SA")]
    [InlineData("zh", "CN")]
    [InlineData("zh-Hant", "TW")]
    [InlineData("ja", "JP")]
    [InlineData("ko", "KR")]
    [InlineData("km", "KH")]
    [InlineData("uk", "UA")]
    [InlineData("el", "GR")]
    [InlineData("cs", "CZ")]
    [InlineData("da", "DK")]
    [InlineData("sv", "SE")]
    [InlineData("hr", "HR")]
    [InlineData("id", "ID")]
    [InlineData("pt", "BR")]
    [InlineData("pt-PT", "PT")]
    [InlineData("nl", "NL")]
    [InlineData("es-419", "ES")]
    [InlineData("", null)]
    [InlineData("not a culture", null)]
    public void Languages_map_to_the_country_of_their_flag(string culture, string? expected) =>
        LanguageFlags.Region(culture).ShouldBe(expected);

    [Fact]
    public void Apps_override_the_country_of_a_language()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = "us", ["pt-BR"] = "BR" };

        LanguageFlags.Region("en", overrides).ShouldBe("US");
        LanguageFlags.Region("en-AU", overrides).ShouldBe("AU", "an explicit region wins over the language's override");
        LanguageFlags.Region("pt-BR", overrides).ShouldBe("BR");
    }

    [Fact]
    public void The_flag_is_a_lazy_image_of_the_library_with_a_text()
    {
        var flag = Render<CountryFlag>(p => p.Add(f => f.Code, "germany").Add(f => f.Size, 24));

        var img = flag.Find("img");
        img.GetAttribute("src").ShouldBe("_content/Coworkee.Client.Blazor/flags/4x3/de.svg");
        img.GetAttribute("loading").ShouldBe("lazy");
        img.GetAttribute("height").ShouldBe("24");
        img.GetAttribute("width").ShouldBe("32");
        img.GetAttribute("alt").ShouldNotBeNullOrWhiteSpace();
        Render<CountryFlag>(p => p.Add(f => f.Code, "Narnia")).Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void The_language_flag_follows_the_overrides_of_the_app()
    {
        Services.GetRequiredService<CoworkeeClientOptions>().LanguageRegions["en"] = "US";

        Render<LanguageFlag>(p => p.Add(f => f.Culture, "en")).Find("img").GetAttribute("src").ShouldEndWith("/us.svg");
        Render<LanguageFlag>(p => p.Add(f => f.Culture, "de")).Find("img").GetAttribute("src").ShouldEndWith("/de.svg");
    }
}
