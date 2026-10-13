using System.Text;
using Coworkee.Contracts.Theming;

namespace Coworkee.Theming.Tests;

public sealed class ThemeLogoTests
{
    [Fact]
    public void Svg_markup_reads_as_utf8_bytes_with_the_svg_content_type()
    {
        ThemeLogo.TryRead("  <svg><g/></svg> ", out var bytes, out var contentType).ShouldBeTrue();

        contentType.ShouldBe("image/svg+xml");
        Encoding.UTF8.GetString(bytes).ShouldBe("<svg><g/></svg>");
    }

    [Fact]
    public void A_data_url_reads_as_its_decoded_bytes_and_content_type()
    {
        ThemeLogo.TryRead("data:image/PNG;base64,AQID", out var bytes, out var contentType).ShouldBeTrue();

        contentType.ShouldBe("image/png");
        bytes.ShouldBe([1, 2, 3]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("logo.png")]
    [InlineData("data:image/gif;base64,AQID")]
    [InlineData("data:image/png;base64,%%%")]
    public void Anything_else_does_not_read(string? logo)
    {
        ThemeLogo.TryRead(logo, out _, out _).ShouldBeFalse();
        ThemeLogo.ToDataUrl(logo).ShouldBeNull();
    }

    [Fact]
    public void A_payload_longer_than_the_base64_of_the_maximum_does_not_read()
    {
        var payload = Convert.ToBase64String(new byte[ThemeLogo.MaxBytes]);

        ThemeLogo.TryRead("data:image/png;base64," + payload, out _, out _).ShouldBeTrue();
        ThemeLogo.TryRead("data:image/png;base64," + payload + "AAAA", out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Svg_markup_becomes_a_base64_data_url()
    {
        ThemeLogo.ToDataUrl("<svg/>").ShouldBe("data:image/svg+xml;base64," + Convert.ToBase64String("<svg/>"u8));
        ThemeLogo.ToDataUrl("data:image/webp;base64,AQID").ShouldBe("data:image/webp;base64,AQID");
    }
}
