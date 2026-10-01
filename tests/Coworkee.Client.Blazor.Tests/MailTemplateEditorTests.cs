using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Mailing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailTemplateEditorTests : ClientTestBase
{
    public MailTemplateEditorTests() =>
        Api.GetMailTemplateAsync("Identity.Welcome", "en", Arg.Any<CancellationToken>()).Returns(
            new MailTemplateDto("Identity.Welcome", "en", "Custom", "<p>custom</p>", "Welcome", "<p>default</p>", true));

    [Fact]
    public async Task Preview_shows_html_rendered_by_the_api()
    {
        Api.PreviewMailTemplateAsync("Identity.Welcome", "en", Arg.Any<SaveMailTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedMailDto("Custom", "<html><p>rendered preview</p></html>"));
        var editor = Render<MailTemplateEditor>(p => p.Add(e => e.Name, "Identity.Welcome").Add(e => e.Culture, "en"));

        await editor.WaitForElement("[data-testid='preview']").ClickAsync(new());

        editor.WaitForAssertion(() => editor.Find("iframe[data-testid='preview-frame']").GetAttribute("srcdoc")!.ShouldContain("rendered preview"));
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
    public async Task Reset_removes_the_override_and_reloads()
    {
        var editor = Render<MailTemplateEditor>(p => p.Add(e => e.Name, "Identity.Welcome").Add(e => e.Culture, "en"));

        await editor.WaitForElement("[data-testid='reset-template']").ClickAsync(new());

        await Api.Received(1).ResetMailTemplateAsync("Identity.Welcome", "en", Arg.Any<CancellationToken>());
        await Api.Received(2).GetMailTemplateAsync("Identity.Welcome", "en", Arg.Any<CancellationToken>());
    }
}
