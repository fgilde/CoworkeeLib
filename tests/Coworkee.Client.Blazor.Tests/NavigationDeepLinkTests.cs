using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class NavigationDeepLinkTests : ClientTestBase
{
    public NavigationDeepLinkTests()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Users", "/admin/users", Icons.Material.Outlined.Person, Group: "Admin"),
            new CoworkeeNavItem("Chat", "/chat", Icons.Material.Outlined.Chat)));
        AddAuthorization().SetAuthorized("Ada");
    }

    [Theory]
    [InlineData("/chat?with=42")]
    [InlineData("/admin/users/42")]
    public async Task Opening_a_deep_link_keeps_it_while_the_menu_marks_its_section(string link)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(link);
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await OpenNavigationAsync(layout);
        layout.WaitForAssertion(() => layout.FindAll("[data-nav='/chat']").ShouldNotBeEmpty());
        Services.GetRequiredService<Security.PermissionStore>().Reset();

        nav.ToBaseRelativePath(nav.Uri).ShouldBe(link.TrimStart('/'));
    }
}
