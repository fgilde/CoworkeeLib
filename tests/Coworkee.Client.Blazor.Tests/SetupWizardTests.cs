using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Contracts.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SetupWizardTests : ClientTestBase
{
    public SetupWizardTests() =>
        Api.GetSetupChecksAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new SetupCheckDto("Database", SetupCheckStatus.Ok, null),
            new SetupCheckDto("Mail", SetupCheckStatus.Warning, "No mail server is configured yet."),
        ]);

    [Fact]
    public async Task Completes_setup_with_entered_values()
    {
        Api.CompleteSetupAsync(Arg.Any<CompleteSetupRequest>(), Arg.Any<CancellationToken>()).Returns(new SetupResultDto(Guid.CreateVersion7(), Guid.CreateVersion7()));
        var wizard = Render<Setup>();

        await FillAsync(wizard, "token-123");

        await Api.Received(1).CompleteSetupAsync(
            Arg.Is<CompleteSetupRequest>(r => r.SetupToken == "token-123" && r.TenantName == "Acme" && r.AdminEmail == "ada@acme.test" && r.AdminPassword == "Admin#12345"
                && (r.Settings == null || r.Settings.Count == 0)),
            Arg.Any<CancellationToken>());
        wizard.WaitForAssertion(() => wizard.Markup.ShouldContain("/bff/login"));
    }

    [Fact]
    public async Task Mail_step_values_are_sent_as_settings()
    {
        Api.CompleteSetupAsync(Arg.Any<CompleteSetupRequest>(), Arg.Any<CancellationToken>()).Returns(new SetupResultDto(Guid.CreateVersion7(), Guid.CreateVersion7()));
        var wizard = Render<Setup>();

        await FillAsync(wizard, "token-123", mail: w =>
        {
            Input(w, "mail-host").Change("smtp.acme.test");
            Input(w, "mail-port").Change("587");
            Input(w, "mail-from").Change("noreply@acme.test");
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
        Input(wizard, "setup-token").Change("token");

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

    private static AngleSharp.Dom.IElement Input(IRenderedComponent<Setup> wizard, string id) =>
        wizard.Find($"input[data-testid='{id}'], [data-testid='{id}'] input");

    private static async Task FillAsync(IRenderedComponent<Setup> wizard, string token, Action<IRenderedComponent<Setup>>? mail = null)
    {
        Input(wizard, "setup-token").Change(token);
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        wizard.WaitForElement("[data-check='Database']");
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        Input(wizard, "tenant-name").Change("Acme");
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        Input(wizard, "admin-email").Change("ada@acme.test");
        Input(wizard, "admin-password").Change("Admin#12345");
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        mail?.Invoke(wizard);
        await wizard.Find("[data-testid='finish']").ClickAsync(new());
    }
}
