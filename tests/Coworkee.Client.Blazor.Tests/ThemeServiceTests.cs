using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
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
    public void Round_trips_every_section_through_json()
    {
        var theme = ThemeMapper.ToTheme(Theme("#123456"));
        theme.Typography.H1.FontFamily = ["Georgia", "serif"];
        theme.Shadows.Elevation[1] = "none";
        theme.LayoutProperties.DrawerWidthLeft = "320px";
        theme.ShowLogoInNav = false;
        theme.NavSingleExpand = true;
        theme.IsPublished = true;

        var request = ThemeMapper.ToRequest("Copy", theme);
        var copy = ThemeMapper.ToTheme(new ThemeDto(Guid.Empty, "Copy", false, false, request.PaletteLight, request.PaletteDark, request.Typography,
            request.LayoutProperties, request.LogoSvg, request.CustomCss, 1, request.Shadows, request.Options, request.IsPublished));

        copy.PaletteLight.Primary.ToString(MudColorOutputFormats.Hex).ShouldBe("#123456");
        copy.Typography.H1.FontFamily.ShouldBe(["Georgia", "serif"]);
        copy.Shadows.Elevation[1].ShouldBe("none");
        copy.LayoutProperties.DrawerWidthLeft.ShouldBe("320px");
        copy.ShowLogoInNav.ShouldBeFalse();
        copy.NavSingleExpand.ShouldBeTrue();
        copy.IsPublished.ShouldBeTrue();
        copy.CustomCss.ShouldBe(".x{}");
        request.Options!.Value.TryGetProperty(nameof(CoworkeeTheme.CustomCss), out _).ShouldBeFalse();
    }

    [Fact]
    public void Dense_is_on_by_default_and_round_trips()
    {
        ThemeMapper.ToTheme(Theme("#123456")).Dense.ShouldBeTrue();
        var theme = ThemeMapper.ToTheme(Theme("#123456"));
        theme.Dense = false;

        var request = ThemeMapper.ToRequest("Loose", theme);

        request.Options!.Value.GetProperty(nameof(CoworkeeTheme.Dense)).GetBoolean().ShouldBeFalse();
        ThemeMapper.ToTheme(Theme("#123456") with { Options = request.Options }).Dense.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{\"DenseTables\":false}", false)]
    [InlineData("{\"DenseTables\":false,\"Dense\":true}", true)]
    public void Reads_the_former_DenseTables_option(string options, bool dense) =>
        ThemeMapper.ToTheme(Theme("#123456") with { Options = JsonDocument.Parse(options).RootElement }).Dense.ShouldBe(dense);

    [Theory]
    [InlineData(true, Margin.Dense)]
    [InlineData(false, null)]
    public async Task Object_edit_fields_follow_the_theme_density(bool dense, Margin? expected)
    {
        var service = new ThemeService(_api);
        service.Preview(new CoworkeeTheme { Dense = dense });
        var meta = new Probe().ObjectEditMeta();

        await new DenseObjectEditMeta<Probe>(service).ConfigureAsync(meta);

        meta.Property(p => p.Name)!.RenderData.Attributes.TryGetValue(nameof(MudTextField<string>.Margin), out var margin);
        ((Margin?)margin).ShouldBe(expected);
    }

    public sealed class Probe
    {
        public string? Name { get; set; }
    }

    private static ThemeDto Theme(string primary) => new(
        Guid.CreateVersion7(), "Brand", false, true,
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Primary"] = primary, ["HoverOpacity"] = 0.1 }),
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Primary"] = "#abcdef" }),
        null,
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["DefaultBorderRadius"] = "2px" }),
        null, ".x{}", 1);
}

public sealed class ThemeMapperDerivedColorTests
{
    [Fact]
    public void Shades_follow_the_theme_colors()
    {
        var dto = new Coworkee.Contracts.Theming.ThemeDto(Guid.CreateVersion7(), "Ocean", true, false,
            System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#1565c0" }),
            System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#64b5f6" }),
            null, null, null, null, 1);

        var theme = Coworkee.Client.Blazor.Theming.ThemeMapper.ToTheme(dto);

        var expected = new MudBlazor.Utilities.MudColor("#1565c0");
        theme.PaletteLight.PrimaryDarken.ShouldBe(expected.ColorRgbDarken().ToString(MudBlazor.Utilities.MudColorOutputFormats.RGB));
    }
}
