using Bunit;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Settings;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ServicesPageTests : ClientTestBase
{
    [Fact]
    public void Shows_a_tile_per_service_with_its_health_and_link()
    {
        Api.GetServicesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ServiceDto("dashboard", "dashboard", "https://localhost:17001", ServiceHealth.Healthy),
            new ServiceDto("mail", "mail", "http://localhost:8025", ServiceHealth.Unreachable),
        ]);
        var auth = AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetPolicies("perm:" + SettingsPermissions.Manage);

        var page = Render<Services>();

        page.WaitForAssertion(() => page.FindAll("[data-service]").Count.ShouldBe(2));
        page.Find("[data-service='mail'] [data-health]").GetAttribute("data-health").ShouldBe("Unreachable");
        var link = page.Find("[data-service='dashboard'] a");
        (link.GetAttribute("href"), link.GetAttribute("target")).ShouldBe(("https://localhost:17001", "_blank"));
    }

    [Theory]
    [InlineData("myapp-api", "Api")]
    [InlineData("pgadmin", "Storage")]
    [InlineData("something", "Cloud")]
    public void Services_get_an_icon_by_name(string name, string icon) =>
        ServiceIcons.For(name).ShouldBe(typeof(Icons.Material.Outlined).GetField(icon)!.GetValue(null));
}
