using Coworkee.Contracts;

namespace Coworkee.Reporting.Tests;

public sealed class WebAddressTests
{
    [Theory]
    [InlineData("https://example.com/a.png", true, true)]
    [InlineData("http://example.com", true, true)]
    [InlineData("/media/a.png", false, true)]
    [InlineData("/", false, true)]
    [InlineData("//evil.test/a.png", false, false)]
    [InlineData("/\\evil.test", false, false)]
    [InlineData("javascript:alert(1)", false, false)]
    [InlineData("data:text/html,x", false, false)]
    [InlineData("file:///etc/passwd", false, false)]
    [InlineData("media/a.png", false, false)]
    [InlineData(null, false, false)]
    public void Paths_are_never_web_addresses_also_on_linux(string? value, bool web, bool webOrLocal)
    {
        WebAddress.IsWeb(value).ShouldBe(web);
        WebAddress.IsWebOrLocalPath(value).ShouldBe(webOrLocal);
    }
}
