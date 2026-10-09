using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ThemesPageTests : ClientTestBase
{
    private static readonly ThemeDto BuiltIn = Theme("Coworkee", isGlobal: true, isDefault: true, isPublished: true);
    private static readonly ThemeDto Own = Theme("Brand", isGlobal: false, isDefault: false, isPublished: false);

    public ThemesPageTests()
    {
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([BuiltIn, Own]);
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(BuiltIn);
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?>());
        JSInterop.Setup<MudBlazor.Extensions.Core.CssVariable[]>("MudExCssHelper.getCssVariables", _ => true).SetResult([]);
        Services.AddCascadingValue(sp => sp.GetRequiredService<ThemeService>().DensitySource);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(ThemePermissions.Manage));
    }

    [Fact]
    public async Task Shows_a_tile_per_theme_and_deletes_only_own_themes_after_confirmation()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForAssertion(() => page.FindAll("[data-testid='theme-tile']").Count.ShouldBe(2));

        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-published']")!.TextContent.Trim().ShouldBe("Published");
        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-delete']").ShouldBeNull();
        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-is-default']").ShouldNotBeNull();
        Tile(page, "Brand").QuerySelector("[data-testid='theme-published']")!.TextContent.Trim().ShouldBe("Not published");

        var deleting = page.Find("[data-theme='Brand'] [data-testid='theme-delete']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Delete").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;

        await Api.Received(1).DeleteThemeAsync(Own.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Edit_opens_the_theme_editor_for_that_theme_and_a_built_in_one_as_copy()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForElement("[data-theme='Coworkee'] [data-testid='theme-edit']");

        _ = page.Find("[data-theme='Coworkee'] [data-testid='theme-edit']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Find("input[data-testid='theme-name'], [data-testid='theme-name'] input").GetAttribute("value").ShouldBe("Coworkee copy"), TimeSpan.FromSeconds(10));
        dialogs.Find("[data-testid='theme-editor']").ChildElementCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Tiles_follow_the_theme_density()
    {
        var page = Render<Themes>();
        page.WaitForAssertion(() => page.Find("[data-testid='theme-published']").ClassList.ShouldContain("mud-chip-size-small"));

        await page.InvokeAsync(() => Services.GetRequiredService<ThemeService>().Preview(new CoworkeeTheme { Dense = false }));

        page.WaitForAssertion(() => page.Find("[data-testid='theme-published']").ClassList.ShouldContain("mud-chip-size-medium"));
    }

    private static AngleSharp.Dom.IElement Tile(IRenderedComponent<Themes> page, string name) => page.Find($"[data-theme='{name}']");

    private static ThemeDto Theme(string name, bool isGlobal, bool isDefault, bool isPublished) => new(
        Guid.CreateVersion7(), name, isGlobal, isDefault,
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456" }),
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#654321" }),
        null, null, null, null, 1, IsPublished: isPublished);
}
