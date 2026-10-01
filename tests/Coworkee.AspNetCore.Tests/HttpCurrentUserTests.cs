using System.Security.Claims;
using Coworkee.AspNetCore.Security;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Tests;

public sealed class HttpCurrentUserTests
{
    [Fact]
    public void Reads_identity_from_claims()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", userId.ToString()), new Claim("tenant", tenantId.ToString()), new Claim("role", "admin")],
                authenticationType: "test")),
        };

        var user = new HttpCurrentUser(new HttpContextAccessor { HttpContext = context });

        user.IsAuthenticated.ShouldBeTrue();
        user.UserId.ShouldBe(userId);
        user.TenantId.ShouldBe(tenantId);
        user.Roles.ShouldBe(["admin"]);
    }

    [Fact]
    public void Anonymous_without_http_context()
    {
        var user = new HttpCurrentUser(new HttpContextAccessor());

        user.IsAuthenticated.ShouldBeFalse();
        user.UserId.ShouldBeNull();
        user.TenantId.ShouldBeNull();
        user.Roles.ShouldBeEmpty();
    }
}
