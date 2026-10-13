using Bunit;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ThemeLogoEditTests : ClientTestBase
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"4\"/></svg>";

    private readonly BunitJSModuleInterop _module;
    private IRenderedComponent<MudDialogProvider> _dialogs = null!;
    private string? _value;
    private int _changes;

    public ThemeLogoEditTests()
    {
        _module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
    }

    private IRenderedComponent<ThemeLogoEdit> RenderEdit(string? value = null) => Render<ThemeLogoEdit>(p => p
        .Add(e => e.Value, value)
        .Add(e => e.ValueChanged, v =>
        {
            _value = v;
            _changes++;
        }));

    private async Task ChooseAsync(IRenderedComponent<ThemeLogoEdit> edit, UploadableFile file)
    {
        _dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        _ = edit.Find("[data-testid='theme-logo-choose']").ClickAsync(new());
        var upload = _dialogs.WaitForComponent<MudExUploadEdit<UploadableFile>>();
        await upload.InvokeAsync(() => upload.Instance.UploadRequestChanged.InvokeAsync(file));
    }

    [Fact]
    public async Task Choosing_an_svg_file_sets_it_as_data_url_and_closes_the_dialog()
    {
        var edit = RenderEdit();

        await ChooseAsync(edit, new UploadableFile { FileName = "logo.svg", ContentType = "image/svg+xml", Data = System.Text.Encoding.UTF8.GetBytes(Svg) });

        edit.WaitForAssertion(() => _value.ShouldBe("data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Svg))));
        _dialogs.WaitForAssertion(() => _dialogs.FindComponents<MudExUploadEdit<UploadableFile>>().ShouldBeEmpty());
    }

    [Fact]
    public async Task A_raster_image_is_shrunk_to_512_pixels_by_the_browser_before_it_is_set()
    {
        _module.Setup<string?>("resizeImage", _ => true).SetResult("data:image/webp;base64,AQID");
        var edit = RenderEdit();

        await ChooseAsync(edit, new UploadableFile { FileName = "logo.png", ContentType = "image/png", Data = [1, 2, 3, 4] });

        edit.WaitForAssertion(() => _value.ShouldBe("data:image/webp;base64,AQID"));
        var call = _module.Invocations["resizeImage"].Single();
        call.Arguments[0].ShouldBe("data:image/png;base64,AQIDBA==");
        call.Arguments[1].ShouldBe(512);
    }

    [Fact]
    public async Task A_shrunk_image_that_is_still_too_big_is_not_set()
    {
        _module.Setup<string?>("resizeImage", _ => true).SetResult("data:image/webp;base64," + Convert.ToBase64String(new byte[ThemeLogo.MaxBytes + 1]));
        var edit = RenderEdit();

        await ChooseAsync(edit, new UploadableFile { FileName = "logo.png", ContentType = "image/png", Data = [1] });

        Services.GetRequiredService<ISnackbar>().ShownSnackbars.ShouldContain(s => s.Message.Contains("at most 512 KB"));
        _changes.ShouldBe(0);
    }

    [Theory]
    [InlineData("photo.gif", "image/gif")]
    [InlineData("page.html", "text/html")]
    public async Task Other_file_types_are_refused(string name, string type)
    {
        var edit = RenderEdit();

        await ChooseAsync(edit, new UploadableFile { FileName = name, ContentType = type, Data = [1, 2, 3] });

        Services.GetRequiredService<ISnackbar>().ShownSnackbars.ShouldContain(s => s.Message.Contains("PNG, JPEG, WebP or SVG"));
        _changes.ShouldBe(0);
    }

    [Fact]
    public async Task An_unreadable_image_is_refused()
    {
        _module.Setup<string?>("resizeImage", _ => true).SetResult(null);
        var edit = RenderEdit();

        await ChooseAsync(edit, new UploadableFile { FileName = "logo.png", ContentType = "image/png", Data = [1] });

        Services.GetRequiredService<ISnackbar>().ShownSnackbars.ShouldContain(s => s.Message.Contains("could not be read"));
        _changes.ShouldBe(0);
    }

    [Fact]
    public async Task Use_app_logo_clears_the_value()
    {
        var edit = RenderEdit("data:image/png;base64,AQID");
        edit.Find("[data-testid='theme-logo-preview']").GetAttribute("src").ShouldBe("data:image/png;base64,AQID");

        await edit.Find("[data-testid='theme-logo-reset']").ClickAsync(new());

        _value.ShouldBeNull();
        _changes.ShouldBe(1);
    }

    [Fact]
    public void Without_a_value_the_app_logo_is_previewed_and_reset_is_disabled()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AppLogo = "app.svg" });
        var edit = RenderEdit();

        edit.Find("[data-testid='theme-logo-preview']").GetAttribute("src").ShouldBe("app.svg");
        edit.Find("[data-testid='theme-logo-reset']").HasAttribute("disabled").ShouldBeTrue();
    }
}