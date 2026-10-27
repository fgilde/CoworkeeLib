using System.Security.Claims;
using Coworkee.AspNetCore.Security;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
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
    public void An_api_keys_limits_stay_inside_an_ambient_user_scope()
    {
        var userId = Guid.CreateVersion7();
        var keyId = Guid.CreateVersion7();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", userId.ToString()), new Claim(ApiKeyDefaults.IdClaim, keyId.ToString()), new Claim(ApiKeyDefaults.ScopeClaim, "Notes.Read")], "ApiKey")),
        };
        var user = new HttpCurrentUser(new HttpContextAccessor { HttpContext = context });

        using var ambient = CurrentUserScope.Begin(new ImpersonatedUser(userId, null));

        user.ApiKeyId.ShouldBe(keyId);
        user.PermissionScopes.ShouldBe(["Notes.Read"]);
    }

    [Fact]
    public void The_impersonator_comes_from_the_token_and_not_into_an_ambient_user_scope()
    {
        var impersonator = Guid.CreateVersion7();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", Guid.CreateVersion7().ToString()), new Claim(ImpersonationClaims.Impersonator, impersonator.ToString())], "test")),
        };
        var user = new HttpCurrentUser(new HttpContextAccessor { HttpContext = context });

        user.ImpersonatorId.ShouldBe(impersonator);
        using var ambient = CurrentUserScope.Begin(new ImpersonatedUser(Guid.CreateVersion7(), null));
        user.ImpersonatorId.ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "Bearer eyJhbGciOi", false)]
    [InlineData("", "Bearer eyJhbGciOi", false)]
    [InlineData("  ", null, false)]
    [InlineData("cwk_abc", null, true)]
    [InlineData(null, "Bearer cwk_abc", true)]
    public void Only_a_present_key_selects_the_key_sign_in(string? header, string? authorization, bool key)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers[ApiKeyDefaults.Header] = header;
        }

        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        Authentication.ApiKeyForwarding.HasApiKey(context.Request).ShouldBe(key);
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
