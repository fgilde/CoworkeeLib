using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailTemplateEditorTests : ClientTestBase
{
    public MailTemplateEditorTests() =>
        Api.GetMailTemplateAsync("Identity.Welcome", "en", Arg.Any<CancellationToken>()).Returns(
            new MailTemplateDto("Identity.Welcome", "en", "Custom", "<p>custom</p>", "Welcome", "<p>default</p>", true, [], false));

    private IRenderedComponent<MailTemplateEditor> RenderEditor() =>
        Render<MailTemplateEditor>(p => p.Add(e => e.Name, "Identity.Welcome").Add(e => e.Culture, "en"));

    private void PreviewRendersSubject() =>
        Api.PreviewMailTemplateAsync("Identity.Welcome", "en", Arg.Any<SaveMailTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new RenderedMailDto(call.Arg<SaveMailTemplateRequest>().Subject, $"<html><head></head><body><p>rendered {call.Arg<SaveMailTemplateRequest>().Subject}</p></body></html>"));

    [Fact]
    public async Task The_preview_sheet_shows_the_rendered_mail_and_follows_typing()
    {
        PreviewRendersSubject();
        var dialogs = Render<MudDialogProvider>();
        var editor = RenderEditor();

        await editor.WaitForElement("[data-testid='preview']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Find("iframe[data-testid='preview-frame']").GetAttribute("srcdoc")!.ShouldContain("rendered Custom"));
        dialogs.Find("[data-testid='preview-subject']").TextContent.ShouldBe("Custom");

        await editor.InvokeAsync(() => editor.FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync("Hello there"));

        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='preview-subject']").TextContent.ShouldBe("Hello there"));
        dialogs.Find("iframe[data-testid='preview-frame']").GetAttribute("srcdoc")!.ShouldContain("rendered Hello there");
    }

    [Fact]
    public async Task The_preview_switches_to_phone_width_and_a_dark_mail_client()
    {
        PreviewRendersSubject();
        var dialogs = Render<MudDialogProvider>();
        var editor = RenderEditor();
        await editor.WaitForElement("[data-testid='preview']").ClickAsync(new());
        dialogs.WaitForElement("iframe[data-testid='preview-frame']");

        await dialogs.Find("[data-testid='preview-phone']").ClickAsync(new());
        await dialogs.Find("[data-testid='preview-dark']").ClickAsync(new());

        var frame = dialogs.Find("iframe[data-testid='preview-frame']");
        frame.GetAttribute("style")!.ShouldContain("width:390px");
        frame.GetAttribute("srcdoc")!.ShouldStartWith("<html><head><style>html{filter:invert(1)");
    }

    [Fact]
    public async Task Leaving_with_unsaved_changes_asks_first()
    {
        var dialogs = Render<MudDialogProvider>();
        var editor = RenderEditor();
        editor.WaitForElement("[data-testid='save-template']");
        editor.FindAll("[data-testid='unsaved']").ShouldBeEmpty();

        await editor.InvokeAsync(() => editor.FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync("Changed"));
        editor.WaitForElement("[data-testid='unsaved']");
        var navigation = Services.GetRequiredService<NavigationManager>();
        _ = editor.InvokeAsync(() => navigation.NavigateTo("/elsewhere"));

        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("Leave without saving?"));
        navigation.Uri.ShouldNotEndWith("/elsewhere");
    }

    [Fact]
    public async Task What_the_editor_rewrites_on_its_own_is_no_change_but_typing_is()
    {
        var editor = RenderEditor();
        editor.WaitForState(() => editor.FindComponents<Components.Editors.RichTextEditor>().Count == 1);
        var visual = editor.FindComponent<Components.Editors.RichTextEditor>();

        await editor.InvokeAsync(() => visual.Instance.ValueChanged.InvokeAsync("<p>custom</p><p></p>"));
        editor.FindAll("[data-testid='unsaved']").ShouldBeEmpty();

        await editor.InvokeAsync(() => editor.Find("[data-testid='mail-visual']").KeyDown(new KeyboardEventArgs { Key = "x" }));
        await editor.InvokeAsync(() => visual.Instance.ValueChanged.InvokeAsync("<p>custom x</p>"));
        editor.WaitForElement("[data-testid='unsaved']");
    }

    [Fact]
    public async Task A_version_from_the_history_goes_back_into_the_editor_unsaved()
    {
        Api.GetMailTemplateHistoryAsync("Identity.Welcome", "en", Arg.Any<CancellationToken>())
            .Returns([new MailTemplateVersionDto(DateTimeOffset.UtcNow, "Ada", "Old subject", "<p>old</p>")]);
        var dialogs = Render<MudDialogProvider>();
        var editor = RenderEditor();

        editor.WaitForElement("[data-testid='history']");
        // the click waits for the dialog to close, so it runs on its own
        _ = editor.InvokeAsync(() => editor.Find("[data-testid='history']").ClickAsync(new()));
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='history-diff']").InnerHtml.ShouldContain("&lt;p&gt;old&lt;/p&gt;"));
        dialogs.FindAll("[data-diff='Removed']").ShouldHaveSingleItem().TextContent.ShouldContain("<p>old</p>");
        dialogs.FindAll("[data-diff='Added']").ShouldHaveSingleItem().TextContent.ShouldContain("<p>custom</p>");
        await dialogs.Find("[data-testid='history-restore']").ClickAsync(new());

        editor.WaitForAssertion(() => editor.FindComponent<MudTextField<string>>().Instance.Value.ShouldBe("Old subject"));
        editor.Find("[data-testid='unsaved']");
        await Api.DidNotReceiveWithAnyArgs().SaveMailTemplateAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Save_shows_syntax_errors_from_the_api()
    {
        Api.SaveMailTemplateAsync("Identity.Welcome", "en", Arg.Any<SaveMailTemplateRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(400, "validation", new Dictionary<string, string[]> { ["Body"] = ["body: <input>(1,4) : error : unexpected end"] }));
        var editor = Render<MailTemplateEditor>(p => p.Add(e => e.Name, "Identity.Welcome").Add(e => e.Culture, "en"));

        await editor.WaitForElement("[data-testid='save-template']").ClickAsync(new());

        editor.WaitForAssertion(() => editor.Find("[data-testid='template-errors']").TextContent.ShouldContain("(1,4)"));
    }

    [Fact]
    public void Custom_templates_have_no_default_to_reset_to()
    {
        Api.GetMailTemplateAsync("News.1", "en", Arg.Any<CancellationToken>()).Returns(
            new MailTemplateDto("News.1", "en", "Hi", "<p>hi</p>", "Hi", "<p>hi</p>", true, [new("user.first_name", "string", "Ada", false)], true));

        var editor = Render<MailTemplateEditor>(p => p.Add(e => e.Name, "News.1").Add(e => e.Culture, "en"));

        editor.WaitForElement("[data-testid='save-template']");
        editor.FindAll("[data-testid='reset-template']").ShouldBeEmpty();
        editor.Find("[data-testid='template-state']").TextContent.ShouldBe("custom");
        editor.FindAll("[data-testid='mail-variable-user.first_name']").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reset_removes_the_override_and_reloads()
    {
        var editor = Render<MailTemplateEditor>(p => p.Add(e => e.Name, "Identity.Welcome").Add(e => e.Culture, "en"));

        await editor.WaitForElement("[data-testid='reset-template']").ClickAsync(new());

        await Api.Received(1).ResetMailTemplateAsync("Identity.Welcome", "en", Arg.Any<CancellationToken>());
        await Api.Received(2).GetMailTemplateAsync("Identity.Welcome", "en", Arg.Any<CancellationToken>());
    }
}
