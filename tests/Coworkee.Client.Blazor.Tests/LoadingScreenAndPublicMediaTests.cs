using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Files;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class LoadingScreenAndPublicMediaTests : ClientTestBase
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>";

    private static readonly ThemeDto Theme = new(Guid.NewGuid(), "Brand", false, true, JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }),
        null, null, null, null, 1);

    private readonly IFilesApi _files = Substitute.For<IFilesApi>();
    private readonly BunitJSModuleInterop _module;
    private readonly StoredFileDto _video = new(Guid.CreateVersion7(), null, "welcome.mp4", "video/mp4", 2048, DateTimeOffset.UtcNow, null);
    private readonly StoredFileDto _logo = new(Guid.CreateVersion7(), null, "logo.svg", "image/svg+xml", 64, DateTimeOffset.UtcNow, null);

    public LoadingScreenAndPublicMediaTests()
    {
        _module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        Services.AddSingleton(_files);
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([FilePermissions.View]);
        Api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?> { [ThemeSettings.Mode] = "dark" });
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).Returns(Design(new SignInDesign { Layout = SignInLayouts.Card }));
        Api.SaveAppConfigurationAsync(SignInDesign.Section, Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(call => Design(call.Arg<JsonElement>().Deserialize<SignInDesign>(JsonSerializerOptions.Web)!));
        _files.GetContentAsync(null, Arg.Any<CancellationToken>()).Returns(new FolderContentDto([], [_video, _logo], true, true));
        _files.SetPublicAsync(_video.Id, true, Arg.Any<CancellationToken>()).Returns(_video with { IsPublic = true });
        _files.OpenReadAsync(_logo.Id, Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Svg)));
    }

    private static AppConfigurationValuesDto Design(SignInDesign design) => new(SignInDesign.Section, JsonSerializer.SerializeToElement(design, JsonSerializerOptions.Web), JsonSerializer.SerializeToElement(new SignInDesign()), []);

    [Fact]
    public void The_layout_hides_the_loading_screen_with_the_users_colour_mode_once_the_theme_is_applied()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Api.GetCurrentThemeAsync(default).ReturnsForAnyArgs(Theme);
        AddAuthorization().SetAuthorized("Ada");

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        // the loading screen leaves the hiding to this layout instead of going with the first content
        layout.FindAll("[data-coworkee-loading-claim]").ShouldHaveSingleItem();
        WaitForAssertion(() => _module.Invocations["hideLoadingScreen"].ShouldHaveSingleItem().Arguments.ShouldBe(["dark", Theme.Id.ToString(), "en"]));
    }

    [Fact]
    public async Task The_sign_in_card_sets_the_loading_screen_and_takes_a_video_from_the_files_after_making_it_public()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var design = new SignInDesign { Layout = SignInLayouts.Card };
        var card = Render<SignInDesignCard>(p => p.Add(c => c.Value, design));
        var select = card.WaitForComponent<MudSelect<LoadingScreenBackground>>();
        await card.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(LoadingScreenBackground.SignInMedia));

        var choosing = card.InvokeAsync(() => card.Find("button[aria-label='Choose from files']").ClickAsync(new()));
        await dialogs.WaitForElement($"[data-file-grid-key='{_video.Id}']").DoubleClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Make public").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Make public").ClickAsync(new());
        await choosing;

        // the page Sign-in saves the design it hands the card
        await _files.Received(1).SetPublicAsync(_video.Id, true, Arg.Any<CancellationToken>());
        design.LoadingScreen.ShouldBe(LoadingScreenBackground.SignInMedia);
        design.MediaUrl.ShouldBe($"http://localhost/api/v1/files/public/{_video.Id}/welcome.mp4");
    }

    [Fact]
    public async Task The_sign_in_card_takes_a_page_background_video_from_the_files_and_sets_its_darkening()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var design = new SignInDesign { Layout = SignInLayouts.Card };
        var card = Render<SignInDesignCard>(p => p.Add(c => c.Value, design));
        var dim = card.WaitForComponent<MudNumericField<int?>>();
        // a stored section from before the field: empty, nothing to darken yet
        dim.Instance.Disabled.ShouldBeTrue();

        var choosing = card.InvokeAsync(() => card.Find("button[aria-label='Choose the page background from files']").ClickAsync(new()));
        await dialogs.WaitForElement($"[data-file-grid-key='{_video.Id}']").DoubleClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Make public").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Make public").ClickAsync(new());
        await choosing;
        card.WaitForAssertion(() => card.FindComponent<MudNumericField<int?>>().Instance.Disabled.ShouldBeFalse());
        await card.InvokeAsync(() => card.FindComponent<MudNumericField<int?>>().Instance.ValueChanged.InvokeAsync(40));

        design.BackgroundUrl.ShouldBe($"http://localhost/api/v1/files/public/{_video.Id}/welcome.mp4");
        design.BackgroundDim.ShouldBe(40);
        design.MediaUrl.ShouldBeNull();
    }

    [Fact]
    public void Without_the_files_permission_the_page_background_has_no_file_button()
    {
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([]);

        var design = new SignInDesign { Layout = SignInLayouts.Card };
        var card = Render<SignInDesignCard>(p => p.Add(c => c.Value, design));

        card.WaitForComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Page background (image or video)").Instance.Adornment.ShouldBe(Adornment.None);
        card.FindAll("button[aria-label='Choose the page background from files']").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_preview_shows_the_real_sign_in_page_with_the_unsaved_design_in_a_frame_without_forms()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var design = new SignInDesign { Layout = SignInLayouts.Card };
        var card = Render<SignInDesignCard>(p => p.Add(c => c.Value, design));
        var background = card.WaitForComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Page background (image or video)");
        await card.InvokeAsync(() => background.Instance.ValueChanged.InvokeAsync("/media/sky.jpg"));
        await card.InvokeAsync(() => card.Find("[data-layout='Slide']").ClickAsync(new()));

        await card.InvokeAsync(() => card.Find("[data-testid='sign-in-preview']").ClickAsync(new()));

        var frame = dialogs.WaitForElement("[data-testid='sign-in-preview-frame']");
        var src = frame.GetAttribute("src")!;
        src.ShouldStartWith("/bff/sign-in-preview?design=");
        var shown = JsonSerializer.Deserialize<SignInDesign>(Uri.UnescapeDataString(src[(src.IndexOf('=') + 1)..]), JsonSerializerOptions.Web)!;
        shown.Layout.ShouldBe(SignInLayouts.Slide);
        shown.BackgroundUrl.ShouldBe("/media/sky.jpg");
        frame.GetAttribute("sandbox").ShouldBe("allow-scripts allow-same-origin");
        await Api.DidNotReceiveWithAnyArgs().SaveAppConfigurationAsync(default!, default, default);
    }

    [Fact]
    public void A_stored_design_without_the_page_background_reads_as_none()
    {
        var design = JsonSerializer.Deserialize<SignInDesign>("""{"layout":"Slide","mediaUrl":"/media/welcome.mp4","mediaSide":"Left"}""", JsonSerializerOptions.Web)!;

        design.BackgroundUrl.ShouldBeNull();
        design.BackgroundDim.ShouldBeNull();
        design.MediaSide.ShouldBe(SignInMediaSide.Left);
    }

    [Fact]
    public async Task The_logo_dialog_takes_an_image_from_the_files()
    {
        string? value = null;
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var edit = Render<ThemeLogoEdit>(p => p.Add(e => e.ValueChanged, v => value = v));
        _ = edit.Find("[data-testid='theme-logo-choose']").ClickAsync(new());

        _ = dialogs.InvokeAsync(() => dialogs.WaitForElement("[data-testid='logo-from-files']").ClickAsync(new()));
        await dialogs.WaitForElement($"[data-file-grid-key='{_logo.Id}']").DoubleClickAsync(new());

        edit.WaitForAssertion(() => value.ShouldBe("data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Svg))));
        dialogs.Markup.ShouldNotContain("welcome.mp4");
    }

    [Fact]
    public async Task A_public_file_is_made_private_again_in_its_preview()
    {
        var shared = _video with { IsPublic = true };
        _files.SetPublicAsync(shared.Id, false, Arg.Any<CancellationToken>()).Returns(_video);
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        await dialogs.InvokeAsync(() => FilePreviewDialog.ShowAsync(Services.GetRequiredService<IDialogService>(), shared));

        await dialogs.InvokeAsync(() => dialogs.WaitForElement("[data-testid='file-make-private']").ClickAsync(new()));

        await _files.Received(1).SetPublicAsync(shared.Id, false, Arg.Any<CancellationToken>());
        dialogs.WaitForAssertion(() => dialogs.FindAll("[data-testid='file-public']").ShouldBeEmpty());
    }

    private void WaitForAssertion(Action assertion)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try
            {
                assertion();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < until)
            {
                Thread.Sleep(50);
            }
        }
    }
}
