using Bunit;
using Coworkee.Client.Blazor.Dialogs;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Options;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DialogConfigurationTests : ClientTestBase
{
    public DialogConfigurationTests() => AddAuthorization().SetAuthorized("Ada");

    [Fact]
    public async Task The_administrator_edits_a_part_and_only_the_changed_parts_are_saved()
    {
        Editable(saved: CoworkeeDialogOptions.Serialize(new Dictionary<DialogPart, DialogOptionsEx> { [DialogPart.Progress] = new() { MaxWidth = MaxWidth.Large } }));
        var page = RenderPage();
        page.WaitForAssertion(() => page.Find("[data-testid='dialog-configuration']"));

        // the first tab is the side sheet, shown with the app's options
        var edit = page.FindComponent<MudExObjectEdit<DialogOptionsEx>>().Instance;
        edit.Value!.MaxWidth.ShouldBe(MaxWidth.Medium);
        edit.Value.Animations = [AnimationType.Perspective3d];
        await page.Find("[data-testid='dialogs-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global, Arg.Is<IReadOnlyDictionary<string, string?>>(v =>
            CoworkeeDialogOptions.Parse(v[DialogSettings.Options]).Keys.Order().SequenceEqual(new[] { DialogPart.SideSheet, DialogPart.Progress })
            && CoworkeeDialogOptions.Parse(v[DialogSettings.Options])[DialogPart.SideSheet].Animations!.SequenceEqual(new[] { AnimationType.Perspective3d })
            && CoworkeeDialogOptions.Parse(v[DialogSettings.Options])[DialogPart.Progress].MaxWidth == MaxWidth.Large), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resetting_a_part_returns_it_to_the_app_and_saves_no_setting_when_nothing_differs()
    {
        Editable(saved: CoworkeeDialogOptions.Serialize(new Dictionary<DialogPart, DialogOptionsEx> { [DialogPart.SideSheet] = new() { MaxWidth = MaxWidth.Large } }));
        var page = RenderPage();
        page.WaitForAssertion(() => page.FindComponent<MudExObjectEdit<DialogOptionsEx>>().Instance.Value!.MaxWidth.ShouldBe(MaxWidth.Large));

        await page.Find("[data-testid='dialog-reset']").ClickAsync(new());
        page.WaitForAssertion(() => page.FindComponent<MudExObjectEdit<DialogOptionsEx>>().Instance.Value!.MaxWidth.ShouldBe(MaxWidth.Medium));
        await page.Find("[data-testid='dialogs-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global, Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[DialogSettings.Options] == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_page_and_its_navigation_entry_need_an_app_that_allows_it()
    {
        (await DialogConfiguration.VisibleAsync(Services)).ShouldBeFalse();
        var page = RenderPage();
        page.Find("[data-testid='dialogs-locked']");
        await Api.DidNotReceiveWithAnyArgs().GetSettingsAsync(default, default);

        var editable = new ServiceCollection().AddSingleton<DialogOptionsContributor>(new EditableDialogs()).BuildServiceProvider();
        (await DialogConfiguration.VisibleAsync(editable)).ShouldBeTrue();
    }

    private IRenderedComponent<DialogConfiguration> RenderPage()
    {
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        Render<MudDialogProvider>();
        return Render<DialogConfiguration>();
    }

    private void Editable(string? saved)
    {
        Services.AddSingleton<DialogOptionsContributor>(new EditableDialogs());
        Api.GetSettingsAsync(SettingScope.Global, default).ReturnsForAnyArgs([new SettingValueDto(DialogSettings.Options, saved, saved is not null)]);
    }

    private sealed class EditableDialogs : DialogOptionsContributor
    {
        public override bool AdminEditable => true;
    }
}
