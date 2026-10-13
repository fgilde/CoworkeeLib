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
        Api.GetServicesAsync(Arg.Any<CancellationToken>()).Returns(new ServiceDirectoryResult(
        [
            new ServiceDto("dashboard", "dashboard", "https://localhost:17001", ServiceHealth.Healthy),
            new ServiceDto("mail", "mail", "http://localhost:8025", ServiceHealth.Unreachable),
        ], []));
        var auth = AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetPolicies("perm:" + SettingsPermissions.Manage);

        var page = Render<Services>();

        page.WaitForAssertion(() => page.FindAll("[data-service]").Count.ShouldBe(2));
        page.Find("[data-service='mail'] [data-health]").GetAttribute("data-health").ShouldBe("Unreachable");
        var link = page.Find("[data-service='dashboard'] a");
        (link.GetAttribute("href"), link.GetAttribute("target")).ShouldBe(("https://localhost:17001", "_blank"));
    }

    [Fact]
    public void Groups_the_services_by_source_and_links_every_url()
    {
        Api.GetServicesAsync(Arg.Any<CancellationToken>()).Returns(new ServiceDirectoryResult(
        [
            new ServiceDto("billing", "billing", "https://billing", ServiceHealth.Healthy),
            new ServiceDto("shop", "shop", "https://shop", ServiceHealth.Healthy, "aspireui", ["https://shop", "https://shop-admin"]),
        ], []));
        var auth = AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetPolicies("perm:" + SettingsPermissions.Manage);

        var page = Render<Services>();

        page.WaitForAssertion(() => page.FindAll("[data-source]").Count.ShouldBe(2));
        page.Find("[data-source='app'] [data-service='billing']").ShouldNotBeNull();
        page.Find("[data-source='aspireui'] h5").TextContent.ShouldBe("AspireUI");
        page.FindAll("[data-source='aspireui'] [data-service='shop'] a").Select(a => a.GetAttribute("href")).ShouldBe(["https://shop", "https://shop-admin"]);
        page.FindAll("[data-source-unavailable]").ShouldBeEmpty();
    }

    [Fact]
    public void Hints_at_every_source_that_could_not_be_asked()
    {
        Api.GetServicesAsync(Arg.Any<CancellationToken>()).Returns(new ServiceDirectoryResult(
            [new ServiceDto("billing", "billing", "https://billing", ServiceHealth.Healthy)], ["aspireui"]));
        var auth = AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetPolicies("perm:" + SettingsPermissions.Manage);

        var page = Render<Services>();

        page.WaitForAssertion(() => page.Find("[data-source-unavailable='aspireui']").TextContent.ShouldContain("AspireUI"));
        page.Find("[data-source='app'] [data-service='billing']").ShouldNotBeNull();
    }

    [Fact]
    public void A_service_without_an_address_looks_and_acts_like_no_link()
    {
        Api.GetServicesAsync(Arg.Any<CancellationToken>()).Returns(new ServiceDirectoryResult(
        [
            new ServiceDto("shop", "shop", "", ServiceHealth.Unhealthy, "aspireui", []),
            new ServiceDto("billing", "billing", "https://billing", ServiceHealth.Healthy, "aspireui", ["https://billing"]),
        ], []));
        var auth = AddAuthorization();
        auth.SetAuthorized("admin");
        auth.SetPolicies("perm:" + SettingsPermissions.Manage);

        var page = Render<Services>();

        page.WaitForAssertion(() => page.FindAll("[data-service]").Count.ShouldBe(2));
        page.Find("[data-service='shop']").ClassList.ShouldNotContain("cursor-pointer");
        page.FindAll("[data-service='shop'] a").ShouldBeEmpty();
        page.Find("[data-service='billing']").ClassList.ShouldContain("cursor-pointer");
    }

    [Theory]
    [InlineData("myapp-api", "Api")]
    [InlineData("pgadmin", "Storage")]
    [InlineData("something", "Cloud")]
    public void Services_get_an_icon_by_name(string name, string icon) =>
        ServiceIcons.For(name).ShouldBe(typeof(Icons.Material.Outlined).GetField(icon)!.GetValue(null));
}
