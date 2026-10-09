using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class HostOnlyNavigationTests : ClientTestBase
{
    public HostOnlyNavigationTests()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Roles", "/admin/roles", Icons.Material.Outlined.Shield, Group: "Admin"),
            new CoworkeeNavItem("Tenants", "/admin/tenants", Icons.Material.Outlined.Domain, Group: "Admin", HostOnly: true)));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData(null, true)]
    public async Task Installation_wide_pages_show_only_in_the_system_organisation(string? systemTenant, bool shown)
    {
        AddAuthorization().SetAuthorized("Ada").SetClaims([.. systemTenant is null ? Array.Empty<Claim>() : [new Claim(SystemTenant.ClaimType, systemTenant)]]);
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await OpenNavigationAsync(layout);

        layout.WaitForAssertion(() => layout.FindAll("[data-nav='/admin/roles']").ShouldNotBeEmpty());
        layout.FindAll("[data-nav='/admin/tenants']").Count.ShouldBe(shown ? 1 : 0);
    }
}
