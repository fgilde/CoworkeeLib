using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailTemplatesPageTests : ClientTestBase
{
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public MailTemplatesPageTests()
    {
        Api.GetMailTemplatesAsync(default).ReturnsForAnyArgs([
            new MailTemplateSummaryDto("Identity.Welcome", "Welcome", ["en", "de"], [], false, "Greets new users"),
            new MailTemplateSummaryDto("News.1", "News", ["en"], ["en"], true, null)]);
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    private IRenderedComponent<MailTemplates> RenderPage(bool system = true) =>
        Render<MailTemplates>(p => p.AddCascadingValue(Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(SystemTenant.ClaimType, system ? "true" : "false")], "test"))))));

    private async Task FillAndSubmit(string name, string displayName)
    {
        // the form re-renders a moment after each change, so finding and acting happen in one step on the renderer thread
        await _dialogs.InvokeAsync(() => InputByLabel(_dialogs.Find("form"), "Name").Change(name));
        await _dialogs.InvokeAsync(() => InputByLabel(_dialogs.Find("form"), "Display name").Change(displayName));
        await _dialogs.InvokeAsync(() => _dialogs.Find("form").SubmitAsync());
    }

    [Fact]
    public async Task New_template_dialog_creates_and_opens_it()
    {
        var page = RenderPage();

        var creating = page.WaitForElement("[data-testid='new-mail-template']").ClickAsync(new());
        _dialogs.WaitForElement("form");
        await FillAndSubmit("News.1", "News");
        await creating;

        await Api.Received(1).CreateMailTemplateAsync(Arg.Is<CreateMailTemplateRequest>(r => r.Name == "News.1" && r.DisplayName == "News" && r.CopyFrom == null && !r.Global), Arg.Any<CancellationToken>());
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/admin/mail/templates/News.1/en");
    }

    [Fact]
    public async Task Duplicate_passes_copy_from()
    {
        var page = RenderPage();

        var duplicating = page.WaitForElement("[data-testid='duplicate-template-Identity.Welcome']").ClickAsync(new());
        _dialogs.WaitForElement("form");
        await _dialogs.Find("form").SubmitAsync();
        await duplicating;

        await Api.Received(1).CreateMailTemplateAsync(Arg.Is<CreateMailTemplateRequest>(r =>
            r.Name == "Identity.Welcome.copy" && r.CopyFrom == "Identity.Welcome" && r.DisplayName == "Welcome (copy)" && r.Description == "Greets new users"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_custom_templates_can_be_deleted()
    {
        var page = RenderPage();

        page.WaitForElement("[data-testid='delete-template-News.1']");
        page.FindAll("[data-testid='delete-template-Identity.Welcome']").ShouldBeEmpty();
        page.FindAll("[data-testid='custom-template-News.1']").Count.ShouldBe(1);

        var deleting = page.Find("[data-testid='delete-template-News.1']").ClickAsync(new());
        _dialogs.WaitForAssertion(() => _dialogs.FindAll("button").ShouldContain(b => b.TextContent.Trim() == "Delete"));
        await _dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;

        await Api.Received(1).DeleteMailTemplateAsync("News.1", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task The_global_checkbox_shows_only_in_the_system_organisation(bool system, int expected)
    {
        var page = RenderPage(system);

        var creating = page.WaitForElement("[data-testid='new-mail-template']").ClickAsync(new());
        _dialogs.WaitForElement("form");

        _dialogs.FindAll("input[type='checkbox']").Count.ShouldBe(expected);
        await _dialogs.Find("button[aria-label='Close']").ClickAsync(new());
        await creating;
    }
}
