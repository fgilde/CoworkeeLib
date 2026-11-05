using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Dialogs;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

// the static helpers read the process-wide CoworkeeDialogOptions.Current, which these tests replace
[CollectionDefinition(nameof(DialogOptionsTests), DisableParallelization = true)]
[Collection(nameof(DialogOptionsTests))]
public sealed class DialogOptionsTests : ClientTestBase
{
    [Fact]
    public void The_library_defaults_are_the_options_the_dialogs_had_before()
    {
        var library = new DialogOptionsContributor();

        Json(library.SideSheet(clickedLeft: true)).ShouldBe(Json(OldSideSheet(DialogPosition.CenterLeft)));
        Json(library.SideSheet(clickedLeft: false)).ShouldBe(Json(OldSideSheet(DialogPosition.CenterRight)));
        Json(library.RightSheet()).ShouldBe(Json(new DialogOptionsEx
        {
            CloseButton = true, CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true, Position = DialogPosition.CenterRight,
            Animations = [AnimationType.SlideIn], FullHeight = true, DisableSizeMarginY = true, DisablePositionMargin = true,
        }));
        Json(library.Small()).ShouldBe(Json(new DialogOptionsEx
        {
            CloseButton = true, CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true, BackdropClick = false,
            Animations = [AnimationType.FadeIn, AnimationType.FlipX],
        }));
        Json(library.Fullscreen()).ShouldBe(Json(new DialogOptionsEx { FullScreen = true, CloseButton = true }));
        Json(library.Progress()).ShouldBe(Json(new DialogOptionsEx
        {
            MaxWidth = MaxWidth.ExtraSmall, FullWidth = true, BackdropClick = false, CloseOnEscapeKey = false, CloseButton = false,
        }));
        Json(library.ListItem()).ShouldBe(Json(new DialogOptionsEx
        {
            MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true, Resizeable = true, DragMode = MudDialogDragMode.Simple,
        }));
        library.AdminEditable.ShouldBeFalse();
    }

    [Fact]
    public void Without_a_provider_the_static_helpers_return_the_library_defaults()
    {
        CoworkeeDialogOptions.Current = null;

        Json(EditDialogExtensions.Small()).ShouldBe(Json(new DialogOptionsContributor().Small()));
        Json(EditDialogExtensions.RightSheet()).ShouldBe(Json(new DialogOptionsContributor().RightSheet()));
    }

    [Fact]
    public async Task The_app_override_wins_over_the_library_default_and_keeps_the_other_parts()
    {
        var options = await ProviderAsync(new AppDialogs(), settings: null);

        options.Get(DialogPart.Small).Animations.ShouldBe([AnimationType.Perspective3d]);
        options.Get(DialogPart.Small).MaxWidth.ShouldBe(MaxWidth.Small);
        Json(options.Get(DialogPart.RightSheet)).ShouldBe(Json(new DialogOptionsContributor().RightSheet()));
    }

    [Fact]
    public async Task The_administrator_setting_wins_over_the_app_when_the_app_allows_it()
    {
        var setting = CoworkeeDialogOptions.Serialize(new Dictionary<DialogPart, DialogOptionsEx> { [DialogPart.Small] = new() { MaxWidth = MaxWidth.Large } });

        var allowed = await ProviderAsync(new AppDialogs { Editable = true }, setting);
        var locked = await ProviderAsync(new AppDialogs(), setting);

        allowed.Get(DialogPart.Small).MaxWidth.ShouldBe(MaxWidth.Large);
        allowed.Get(DialogPart.Small).Animations.ShouldBe(new DialogOptionsEx().Animations); // the part is replaced, not merged with the app's
        allowed.Get(DialogPart.Progress).MaxWidth.ShouldBe(MaxWidth.ExtraSmall);
        locked.Get(DialogPart.Small).MaxWidth.ShouldBe(MaxWidth.Small);
    }

    [Fact]
    public async Task A_side_sheet_set_by_the_administrator_still_opens_on_the_clicked_side()
    {
        var setting = CoworkeeDialogOptions.Serialize(new Dictionary<DialogPart, DialogOptionsEx>
        {
            [DialogPart.SideSheet] = new() { MaxWidth = MaxWidth.Large, Position = DialogPosition.TopCenter },
        });

        var options = await ProviderAsync(new AppDialogs { Editable = true }, setting);

        options.Get(DialogPart.SideSheet, clickedLeft: true).Position.ShouldBe(DialogPosition.CenterLeft);
        options.Get(DialogPart.SideSheet, clickedLeft: false).Position.ShouldBe(DialogPosition.CenterRight);
        options.Get(DialogPart.SideSheet).MaxWidth.ShouldBe(MaxWidth.Large);
    }

    [Fact]
    public void A_broken_setting_is_ignored()
    {
        CoworkeeDialogOptions.Parse("{not json").ShouldBeEmpty();
        CoworkeeDialogOptions.Parse("""{"Nope":{},"Small":{"maxWidth":"Large"}}""").Keys.ShouldBe([DialogPart.Small]);
    }

    [Fact]
    public async Task A_confirmation_of_the_library_uses_the_provider()
    {
        CoworkeeDialogOptions.Current = await ProviderAsync(new AppDialogs { SmallWidth = MaxWidth.ExtraLarge }, settings: null);
        try
        {
            Render<MudPopoverProvider>();
            var provider = Render<MudDialogProvider>();

            _ = provider.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ConfirmAsync("Delete", "Sure?", "Delete", "Cancel"));

            provider.WaitForAssertion(() => provider.Find(".mud-dialog-width-xl"));
        }
        finally
        {
            CoworkeeDialogOptions.Current = null;
        }
    }

    [Fact]
    public async Task A_side_sheet_edit_of_the_library_uses_the_provider()
    {
        CoworkeeDialogOptions.Current = await ProviderAsync(new AppDialogs { SideSheetWidth = MaxWidth.ExtraSmall }, settings: null);
        try
        {
            Render<MudPopoverProvider>();
            var provider = Render<MudDialogProvider>();

            _ = provider.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowEditAsync("Edit", new Form()));

            provider.WaitForAssertion(() => provider.Find(".mud-dialog-width-xs"));
        }
        finally
        {
            CoworkeeDialogOptions.Current = null;
        }
    }

    private static async Task<CoworkeeDialogOptions> ProviderAsync(DialogOptionsContributor contributor, string? settings)
    {
        var api = Substitute.For<Api.ICoworkeeApi>();
        api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?> { [DialogSettings.Options] = settings });
        var themes = new ThemeService(api);
        await themes.LoadAsync();
        return new CoworkeeDialogOptions(contributor, themes);
    }

    private static string Json(DialogOptionsEx options) => JsonSerializer.Serialize(options);

    private static DialogOptionsEx OldSideSheet(DialogPosition position) => new()
    {
        CloseButton = true,
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        BackdropClick = false,
        MaximizeButton = true,
        DragMode = MudDialogDragMode.Simple,
        Position = position,
        Animations = [AnimationType.SlideIn],
        FullHeight = true,
        DisableSizeMarginY = true,
        DisablePositionMargin = true,
    };

    private sealed class Form
    {
        public string Name { get; set; } = "Ada";
    }

    private sealed class AppDialogs : DialogOptionsContributor
    {
        public bool Editable { get; init; }

        public MaxWidth? SmallWidth { get; init; }

        public MaxWidth? SideSheetWidth { get; init; }

        public override bool AdminEditable => Editable;

        public override DialogOptionsEx Small()
        {
            var options = base.Small();
            options.Animations = [AnimationType.Perspective3d];
            options.MaxWidth = SmallWidth ?? options.MaxWidth;
            return options;
        }

        public override DialogOptionsEx SideSheet(bool clickedLeft)
        {
            var options = base.SideSheet(clickedLeft);
            options.MaxWidth = SideSheetWidth ?? options.MaxWidth;
            return options;
        }
    }
}
