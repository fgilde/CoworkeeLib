using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SetupWizardTests : ClientTestBase
{
    private static readonly ThemeDto Coworkee = Theme("Coworkee", "#d4481f", isDefault: true);
    private static readonly ThemeDto Ocean = Theme("Ocean", "#1565c0");

    public SetupWizardTests()
    {
        Api.GetSetupChecksAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new SetupCheckDto("Database", SetupCheckStatus.Ok, null),
            new SetupCheckDto("Mail", SetupCheckStatus.Warning, "No mail server is configured yet."),
        ]);
        Api.GetBuiltInThemesAsync(Arg.Any<CancellationToken>()).Returns([Coworkee, Ocean]);
        Api.CompleteSetupAsync(Arg.Any<CompleteSetupRequest>(), Arg.Any<CancellationToken>()).Returns(new SetupResultDto(Guid.CreateVersion7(), Guid.CreateVersion7()));
    }

    private static ThemeDto Theme(string name, string primary, bool isDefault = false) => new(
        Guid.CreateVersion7(), name, true, isDefault,
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = primary, ["Background"] = "#ffffff" }),
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = primary, ["Background"] = "#000000" }),
        null, null, null, null, 1);

    [Fact]
    public async Task Completes_setup_with_entered_values()
    {
        var wizard = Render<Setup>();

        await FillAsync(wizard, "token-123");

        await Api.Received(1).CompleteSetupAsync(
            Arg.Is<CompleteSetupRequest>(r => r.SetupToken == "token-123" && r.TenantName == "Acme" && r.AdminEmail == "ada@acme.test" && r.AdminPassword == "Admin#12345"
                && (r.Settings == null || r.Settings.Count == 0) && r.ThemeId == null),
            Arg.Any<CancellationToken>());
        wizard.WaitForAssertion(() => wizard.Find("[data-testid='sign-in']").GetAttribute("href")!.ShouldContain("/bff/login?returnUrl=%2F&loginHint=ada%40acme.test"));
    }

    [Fact]
    public async Task Chosen_theme_and_mode_apply_at_once_and_become_the_default()
    {
        var wizard = Render<Setup>();

        await FillAsync(wizard, "token-123", look: async w =>
        {
            w.WaitForElement("[data-theme='Ocean']");
            w.FindAll("[data-theme='Ocean'] .cw-swatch span").Select(e => e.GetAttribute("style")).ShouldBe(["background:#1565c0", "background:#ffffff"]);
            await w.Find("[data-theme='Ocean']").ClickAsync(new());
            await w.Find("[data-mode='dark']").ClickAsync(new());
            var themes = Services.GetRequiredService<ThemeService>();
            (themes.Current!.Name, themes.Mode).ShouldBe(("Ocean", "dark"));
        });

        await Api.Received(1).CompleteSetupAsync(
            Arg.Is<CompleteSetupRequest>(r => r.ThemeId == Ocean.Id && r.Settings != null && r.Settings["Theme.Mode"] == "dark"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Next_stays_on_the_administrator_step_until_the_passwords_match()
    {
        var wizard = Render<Setup>();
        await ToAdministratorAsync(wizard, "token-123");
        Input(wizard, "admin-email").Input("ada@acme.test");
        Input(wizard, "admin-password").Input("Admin#12345");
        Input(wizard, "admin-password-confirm").Input("Admin#1234");

        await wizard.Find("[data-testid='next']").ClickAsync(new());

        wizard.Find("[data-testid='setup-error']").TextContent.ShouldContain("do not match");
        wizard.FindAll("input[data-testid='admin-password'], [data-testid='admin-password'] input").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Next_never_looks_disabled_on_steps_that_can_be_left()
    {
        var wizard = Render<Setup>();

        wizard.Find("[data-testid='next']").HasAttribute("disabled").ShouldBeFalse();
        await wizard.Find("[data-testid='next']").ClickAsync(new());

        wizard.Find("[data-testid='setup-error']").TextContent.ShouldContain("setup token");
    }

    [Fact]
    public async Task Mail_step_values_are_sent_as_settings()
    {
        var wizard = Render<Setup>();

        await FillAsync(wizard, "token-123", mail: w =>
        {
            Input(w, "mail-host").Input("smtp.acme.test");
            Input(w, "mail-port").Input("587");
            Input(w, "mail-from").Input("noreply@acme.test");
        });

        await Api.Received(1).CompleteSetupAsync(
            Arg.Is<CompleteSetupRequest>(r => r.Settings != null && r.Settings["Mail.Smtp.Host"] == "smtp.acme.test" && r.Settings["Mail.Smtp.Port"] == "587"
                && r.Settings["Mail.From"] == "noreply@acme.test" && !r.Settings.ContainsKey("Mail.Smtp.Password")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Checks_step_shows_results_and_blocks_on_errors()
    {
        Api.GetSetupChecksAsync(Arg.Any<CancellationToken>()).Returns([new SetupCheckDto("Database", SetupCheckStatus.Error, "The database is not reachable.")]);
        var wizard = Render<Setup>();
        Input(wizard, "setup-token").Input("token");

        await wizard.Find("[data-testid='next']").ClickAsync(new());

        wizard.WaitForAssertion(() => wizard.Find("[data-check='Database']").TextContent.ShouldContain("not reachable"));
        wizard.Find("[data-testid='next']").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task Wrong_token_shows_error_and_returns_to_first_step()
    {
        Api.CompleteSetupAsync(Arg.Any<CompleteSetupRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ApiException(403, "setup.token_invalid", null));
        var wizard = Render<Setup>();

        await FillAsync(wizard, "wrong");

        wizard.WaitForAssertion(() => wizard.Find("[data-testid='setup-error']").TextContent.ShouldNotBeNullOrWhiteSpace());
        wizard.FindAll("[data-testid='setup-token']").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Password_rules_from_the_server_lead_back_to_the_administrator_step()
    {
        Api.CompleteSetupAsync(Arg.Any<CompleteSetupRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(400, "validation", new Dictionary<string, string[]> { ["Password"] = ["Passwords must have at least one digit."] }));
        var wizard = Render<Setup>();

        await FillAsync(wizard, "token-123");

        wizard.WaitForAssertion(() => wizard.Markup.ShouldContain("at least one digit"));
        wizard.FindAll("input[data-testid='admin-password'], [data-testid='admin-password'] input").ShouldNotBeEmpty();
    }

    [Fact]
    public void Setup_runs_in_its_own_layout_without_navigation()
    {
        typeof(Setup).GetCustomAttributes(typeof(LayoutAttribute), true).Cast<LayoutAttribute>().Single().LayoutType.ShouldBe(typeof(SetupLayout));

        var layout = Render<SetupLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.FindAll(".mud-nav-menu, .mud-drawer").ShouldBeEmpty();
        layout.Markup.ShouldContain("body");
    }

    private static AngleSharp.Dom.IElement Input(IRenderedComponent<Setup> wizard, string id) =>
        wizard.Find($"input[data-testid='{id}'], [data-testid='{id}'] input");

    private static async Task ToAdministratorAsync(IRenderedComponent<Setup> wizard, string token, Func<IRenderedComponent<Setup>, Task>? look = null)
    {
        Input(wizard, "setup-token").Input(token);
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        wizard.WaitForElement("[data-check='Database']");
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        Input(wizard, "tenant-name").Input("Acme");
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        if (look is not null)
        {
            await look(wizard);
        }

        await wizard.Find("[data-testid='next']").ClickAsync(new());
    }

    private static async Task FillAsync(IRenderedComponent<Setup> wizard, string token, Action<IRenderedComponent<Setup>>? mail = null, Func<IRenderedComponent<Setup>, Task>? look = null)
    {
        await ToAdministratorAsync(wizard, token, look);
        Input(wizard, "admin-email").Input("ada@acme.test");
        Input(wizard, "admin-password").Input("Admin#12345");
        Input(wizard, "admin-password-confirm").Input("Admin#12345");
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        mail?.Invoke(wizard);
        await wizard.Find("[data-testid='finish']").ClickAsync(new());
    }
}
