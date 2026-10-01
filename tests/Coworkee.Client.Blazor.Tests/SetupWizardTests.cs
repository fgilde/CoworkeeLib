using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Contracts.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SetupWizardTests : ClientTestBase
{
    [Fact]
    public async Task Completes_setup_with_entered_values()
    {
        Api.CompleteSetupAsync(Arg.Any<CompleteSetupRequest>(), Arg.Any<CancellationToken>()).Returns(new SetupResultDto(Guid.CreateVersion7(), Guid.CreateVersion7()));
        var wizard = Render<Setup>();

        await FillAsync(wizard, "token-123");

        await Api.Received(1).CompleteSetupAsync(
            Arg.Is<CompleteSetupRequest>(r => r.SetupToken == "token-123" && r.TenantName == "Acme" && r.AdminEmail == "ada@acme.test" && r.AdminPassword == "Admin#12345"),
            Arg.Any<CancellationToken>());
        wizard.WaitForAssertion(() => wizard.Markup.ShouldContain("/bff/login"));
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

    private static async Task FillAsync(IRenderedComponent<Setup> wizard, string token)
    {
        Input(wizard, "setup-token").Change(token);
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        Input(wizard, "tenant-name").Change("Acme");
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        Input(wizard, "admin-email").Change("ada@acme.test");
        Input(wizard, "admin-password").Change("Admin#12345");
        await wizard.Find("[data-testid='finish']").ClickAsync(new());
    }
}
