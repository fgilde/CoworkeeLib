using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CurrentUserInfoTests : ClientTestBase
{
    [Fact]
    public async Task The_current_user_comes_from_the_authentication_state_and_reports_changes()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var auth = AddAuthorization();
        auth.SetNotAuthorized();
        Services.AddScoped<CurrentUserInfo>();
        var info = Services.GetRequiredService<CurrentUserInfo>();
        ClientUser? changed = null;
        info.Changed += u => changed = u;

        (await info.GetAsync()).ShouldBe(ClientUser.Anonymous);

        auth.SetAuthorized("Ada").SetClaims(new Claim("sub", userId.ToString()), new Claim(ClientUser.TenantClaimType, tenantId.ToString()), new Claim("email", "ada@acme.test"));

        var user = await info.GetAsync();
        user.ShouldBe(new ClientUser(true, userId, tenantId, "ada@acme.test", "Ada"));
        changed.ShouldBe(user);
    }
}
