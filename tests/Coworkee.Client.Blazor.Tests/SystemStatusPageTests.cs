using Bunit;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SystemStatusPageTests : ClientTestBase
{
    [Fact]
    public async Task Shows_the_sections_with_their_levels_problems_and_links_and_refreshes()
    {
        Services.AddSingleton(new CoworkeePages(LibraryPages.All, new CoworkeeClientOptions()));
        Api.GetSystemStatusAsync(Arg.Any<CancellationToken>()).Returns(new SystemStatusDto(DateTimeOffset.UtcNow,
        [
            new SystemStatusSection("jobs", "Background jobs", [new("Failed", "2", SystemStatusLevel.Error)],
                [new SystemStatusProblem("MailJob.Send", "SMTP down")], Link: "/admin/jobs", ExternalLink: true),
            new SystemStatusSection("mail", "Mail", [SystemStatusEntry.Text("Provider", "Connected", SystemStatusLevel.Ok)], Link: "/admin/mail/log"),
        ]));
        var auth = AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetPolicies("perm:" + SettingsPermissions.Manage);

        var page = Render<SystemStatus>();

        page.WaitForAssertion(() => page.FindAll("[data-section]").Count.ShouldBe(2));
        page.Find("[data-section='jobs'] [data-entry='Failed']").GetAttribute("data-level").ShouldBe("Error");
        page.Find("[data-problem='jobs']").TextContent.ShouldContain("SMTP down");
        var jobs = page.Find("[data-link='jobs']");
        (jobs.GetAttribute("href"), jobs.GetAttribute("target")).ShouldBe(("/admin/jobs", "_blank"));
        page.Find("[data-link='mail']").GetAttribute("href").ShouldBe("/cw/admin/mail/log");

        await page.Find("[data-testid='refresh-status']").ClickAsync(new());
        await Api.Received(2).GetSystemStatusAsync(Arg.Any<CancellationToken>());
    }
}
