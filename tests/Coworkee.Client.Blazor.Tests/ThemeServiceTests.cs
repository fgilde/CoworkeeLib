using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using MudBlazor;
using MudBlazor.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ThemeServiceTests
{
    private readonly ICoworkeeApi _api = Substitute.For<ICoworkeeApi>();

    [Fact]
    public async Task Applies_the_current_theme_from_the_api()
    {
        _api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(Theme("#123456"));
        var service = new ThemeService(_api);

        await service.LoadAsync();

        service.Theme.PaletteLight.Primary.ToString(MudColorOutputFormats.Hex).ShouldBe("#123456");
        service.Theme.LayoutProperties.DefaultBorderRadius.ShouldBe("2px");
        service.CustomCss.ShouldBe(".x{}");
    }

    [Fact]
    public async Task Falls_back_to_the_built_in_theme_when_the_api_fails()
    {
        _api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        var service = new ThemeService(_api);

        await service.LoadAsync();

        service.Theme.ShouldBeSameAs(CoworkeeTheme.Default);
    }

    [Fact]
    public async Task Uses_the_mode_from_the_user_settings()
    {
        _api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(Theme("#123456"));
        _api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?> { [ThemeSettings.Mode] = "dark" });
        var service = new ThemeService(_api);

        await service.LoadAsync();

        service.Mode.ShouldBe("dark");
    }

    [Fact]
    public void Round_trips_a_palette_through_json()
    {
        var theme = ThemeMapper.ToMudTheme(Theme("#123456"));

        var request = ThemeMapper.ToRequest("Copy", theme, null, null);

        ThemeMapper.ToMudTheme(new ThemeDto(Guid.Empty, "Copy", false, false, request.PaletteLight, request.PaletteDark, null, request.LayoutProperties, null, null, 1))
            .PaletteLight.Primary.ToString(MudColorOutputFormats.Hex).ShouldBe("#123456");
    }

    private static ThemeDto Theme(string primary) => new(
        Guid.CreateVersion7(), "Brand", false, true,
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Primary"] = primary, ["HoverOpacity"] = 0.1 }),
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Primary"] = "#abcdef" }),
        null,
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["DefaultBorderRadius"] = "2px" }),
        null, ".x{}", 1);
}
