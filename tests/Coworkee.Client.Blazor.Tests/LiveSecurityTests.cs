using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Permissions pushed by the server reach navigation, guards and the role switch without a reload; the real policy pipeline runs.</summary>
public sealed class LiveSecurityTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly MyRoleDto Brands = new(Guid.CreateVersion7(), "Brand Manager", "Maintains brands", true);
    private static readonly MyRoleDto Products = new(Guid.CreateVersion7(), "Product Manager", null, false);
    private readonly IRenderedComponent<MudPopoverProvider> _popovers;

    public LiveSecurityTests()
    {
        Services.AddAuthorizationCore();
        Services.AddScoped<IAuthorizationService, DefaultAuthorizationService>();
        Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        Services.AddScoped<BffAuthenticationStateProvider>();
        Services.AddScoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<BffAuthenticationStateProvider>());
        Api.GetUserAsync(Arg.Any<CancellationToken>()).Returns(new BffUserDto(true, "Ada", "ada@acme.test", Me, null, []));
        _popovers = Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }


    private FakeRealtimeConnection Realtime => Services.GetRequiredService<FakeRealtimeConnection>();

    [Fact]
    public void A_pushed_permission_change_flips_the_guard_both_ways()
    {
        Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns([], ["Catalog.Brands.View"], []);
        var view = Render<CascadingAuthenticationState>(p => p.AddChildContent<PermissionView>(v => v
            .Add(c => c.Permission, "Catalog.Brands.View")
            .Add(c => c.ChildContent, "<p id='page'>brands</p>")
            .Add(c => c.NotAuthorized, "<p id='denied'>no</p>")));
        view.WaitForAssertion(() => view.Find("#denied"));
        view.WaitForAssertion(() => Realtime.Invocations.ShouldContain(("Subscribe", RealtimeTopics.TenantPermissions)));

        Realtime.Push(RealtimeTopics.TenantPermissions);
        view.WaitForAssertion(() => view.Find("#page"));

        Realtime.Push(RealtimeTopics.GlobalPermissions);
        view.WaitForAssertion(() => view.Find("#denied"));
    }

    [Fact]
    public void The_role_switch_shows_with_several_roles_and_switches()
    {
        Api.GetMyRolesAsync(null, Arg.Any<CancellationToken>()).Returns(new MyRolesDto(true, null, [Brands, Products]));
        var menu = Render<ActiveRoleMenu>();
        menu.WaitForAssertion(() => menu.Find("[data-testid='active-role']").TextContent.ShouldContain("All roles"));

        menu.Find("[data-testid='active-role']").Click();
        _popovers.WaitForAssertion(() => _popovers.Find($"[data-role='{Brands.Id}']"));
        _popovers.Find($"[data-role='{Brands.Id}']").Click();

        menu.WaitForAssertion(() => Api.Received(1).SetActiveRoleAsync(Brands.Id, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void The_role_switch_hides_with_one_role_or_when_switched_off()
    {
        Api.GetMyRolesAsync(null, Arg.Any<CancellationToken>()).Returns(new MyRolesDto(true, null, [Brands]), new MyRolesDto(false, null, [Brands, Products]));

        Render<ActiveRoleMenu>().Markup.Trim().ShouldBeEmpty();
        Render<ActiveRoleMenu>().Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void Not_allowed_offers_the_roles_that_grant_the_page_and_switches_to_one()
    {
        Api.GetMyRolesAsync("Catalog.Brands.View", Arg.Any<CancellationToken>()).Returns(new MyRolesDto(true, Products.Id, [Brands, Products]));
        Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var page = Render<CascadingAuthenticationState>(p => p.AddChildContent<NotAllowed>(n => n.Add(c => c.PageType, typeof(BrandsPage))));

        page.WaitForAssertion(() => page.Find($"[data-switch-role='{Brands.Id}']"));
        page.Find($"[data-switch-role='{Brands.Id}']").Click();

        page.WaitForAssertion(() => Api.Received(1).SetActiveRoleAsync(Brands.Id, Arg.Any<CancellationToken>()));
        page.FindAll($"[data-switch-role='{Products.Id}']").ShouldBeEmpty();
    }

    [Authorize(Policy = PermissionPolicy.Prefix + "Catalog.Brands.View")]
    private sealed class BrandsPage;
}
