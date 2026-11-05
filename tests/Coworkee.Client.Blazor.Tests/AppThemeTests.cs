using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;
using MudBlazor.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>An app registers its own theme class with AddCoworkeeTheme: it is created, stored, loaded and edited instead of CoworkeeTheme.</summary>
public sealed class AppThemeTests : ClientTestBase
{
    private static readonly ThemeDto Own = new(Guid.CreateVersion7(), "Brand", false, false,
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456" }),
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#654321" }),
        null, null, null, null, 1, Options: JsonSerializer.SerializeToElement(new { Motto = "Ship it", Dense = false }));

    [Fact]
    public void The_mapper_reads_and_writes_the_properties_of_the_app_theme()
    {
        var theme = ThemeMapper.ToTheme(Own, typeof(ShopTheme)).ShouldBeOfType<ShopTheme>();
        theme.Motto.ShouldBe("Ship it");
        theme.Dense.ShouldBeFalse();
        theme.Motto = "Sail it";

        var options = ThemeMapper.ToRequest("Brand", theme).Options!.Value;

        options.GetProperty(nameof(ShopTheme.Motto)).GetString().ShouldBe("Sail it");
        options.GetProperty(nameof(CoworkeeTheme.Dense)).GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_theme_service_uses_the_app_theme_also_before_and_without_a_stored_theme()
    {
        Services.AddCoworkeeTheme<ShopTheme, ShopThemeEditMeta>();
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        var service = Services.GetRequiredService<ThemeService>();

        service.Theme.ShouldBeOfType<ShopTheme>().Motto.ShouldBe("Ahoy");
        service.Theme.PaletteLight.Primary.ToString(MudColorOutputFormats.Hex).ShouldBe(CoworkeeTheme.Default.PaletteLight.Primary.ToString(MudColorOutputFormats.Hex));
        await service.LoadAsync();
        service.Theme.ShouldBeOfType<ShopTheme>();

        service.Apply(Own);

        service.Theme.ShouldBeOfType<ShopTheme>().Motto.ShouldBe("Ship it");
    }

    [Fact]
    public void The_theme_editor_shows_the_app_properties_through_the_app_meta_next_to_the_library_ones()
    {
        Services.AddCoworkeeTheme<ShopTheme, ShopThemeEditMeta>();
        Services.AddSingleton(new CoworkeeClientOptions { AllowSignInDesign = false });
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([Own]);
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(Own);
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?>());
        JSInterop.Setup<MudBlazor.Extensions.Core.CssVariable[]>("MudExCssHelper.getCssVariables", _ => true).SetResult([]);
        Services.AddCascadingValue(sp => sp.GetRequiredService<ThemeService>().DensitySource);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(ThemePermissions.Manage));
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForElement("[data-theme='Brand'] [data-testid='theme-edit']");

        _ = page.Find("[data-theme='Brand'] [data-testid='theme-edit']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("Motto of the shop"), TimeSpan.FromSeconds(30)); // the theme editor renders a lot
        dialogs.Find("[data-testid='theme-logo']").ShouldNotBeNull();
        dialogs.FindAll(".mud-ex-property-container-for-ShowLogoInNav").ShouldNotBeEmpty(); // CoworkeeTheme's own properties stay in the simple mode
        dialogs.FindAll(".mud-ex-property-container-for-Dense").ShouldNotBeEmpty();

        // without a grid item the first row slid under the panel above, its labels cut off at the top
        foreach (var property in new[] { "ShowLogoInNav", "Dense", "IsPublished", "Motto" })
        {
            dialogs.Find($".mud-ex-property-container-for-{property}").ParentElement!.ClassList.ShouldContain("mud-grid-item", property);
        }
    }

    public sealed class ShopTheme : CoworkeeTheme
    {
        public string Motto { get; set; } = "Ahoy";
    }

    public sealed class ShopThemeEditMeta(CoworkeeLocalizer l) : CoworkeeThemeEditMeta<ShopTheme>(l)
    {
        public override async Task ConfigureAsync(ObjectEditMeta<ShopTheme> meta)
        {
            await base.ConfigureAsync(meta);
            meta.Property(t => t.Motto).WithLabel("Motto of the shop");
        }
    }
}
