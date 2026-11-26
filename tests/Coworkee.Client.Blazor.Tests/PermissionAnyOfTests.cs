using System.Security.Claims;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

// a page or navigation entry reachable with any of several permissions: PermissionPolicy.AnyOf, through the real policy pipeline
public sealed class PermissionAnyOfTests : ClientTestBase
{
    public PermissionAnyOfTests()
    {
        Services.AddAuthorizationCore();
        Services.AddScoped<IAuthorizationService, DefaultAuthorizationService>();
        Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
    }

    [Theory]
    [InlineData("B", true)]
    [InlineData("C", true)]
    [InlineData("D", false)]
    public async Task Any_one_of_the_permissions_opens_the_page_and_shows_the_entry(string granted, bool allowed)
    {
        Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns([granted]);
        var user = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var authorization = Services.GetRequiredService<IAuthorizationService>();
        var permission = PermissionPolicy.AnyOf("A", "B", "C");

        (await authorization.AuthorizeAsync(user, PermissionPolicy.For(permission))).Succeeded.ShouldBe(allowed);
        var items = await NavigationAccess.AllowedAsync([new CoworkeeNavItem("Data types", "/x", "i", permission)], user, authorization, Services.GetRequiredService<Features.FeatureStore>());
        items.Count.ShouldBe(allowed ? 1 : 0);
    }
}
