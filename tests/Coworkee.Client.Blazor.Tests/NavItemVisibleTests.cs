using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class NavItemVisibleTests : ClientTestBase
{
    public sealed record Leader(bool Leads);

    public NavItemVisibleTests()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Services.AddSingleton(new Leader(false));
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Roles", "/admin/roles", Icons.Material.Outlined.Shield, Group: "Admin"),
            new CoworkeeNavItem("Team", "/team", Icons.Material.Outlined.Groups, Visible: sp => Task.FromResult(sp.GetRequiredService<Leader>().Leads)),
            new CoworkeeNavItem("Always", "/always", Icons.Material.Outlined.Star, Visible: _ => Task.FromResult(true))));
    }

    [Fact]
    public async Task Items_show_only_when_their_visibility_callback_agrees()
    {
        AddAuthorization().SetAuthorized("Ada");
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await OpenNavigationAsync(layout);

        layout.WaitForAssertion(() => layout.FindAll("[data-nav='/always']").ShouldNotBeEmpty());
        layout.FindAll("[data-nav='/team']").ShouldBeEmpty();
    }
}
