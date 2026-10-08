using Coworkee.Client.Blazor.Localization;

namespace Coworkee.Client.Blazor.Tests;

public sealed class PropertyLabelsTests
{
    [Theory]
    [InlineData("ShowLogoInNav", "Show logo in nav")]
    [InlineData("FirstName", "First name")]
    [InlineData("AppbarHeight", "Appbar height")]
    [InlineData("CustomCss", "Custom css")]
    [InlineData("ShowUICard", "Show UI card")]
    [InlineData("H1", "H1")]
    [InlineData("DrawerWidthLeft", "Drawer width left")]
    public void Humanizes_property_names(string name, string label) => PropertyLabels.Humanize(name).ShouldBe(label);
}
