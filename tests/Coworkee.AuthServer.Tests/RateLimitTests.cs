using Coworkee.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed class RateLimitTests(AuthApp app)
{
    [Fact]
    public void The_sign_in_pages_carry_the_auth_policy_and_the_token_endpoint_does_not()
    {
        var endpoints = app.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        string? Policy(string route) => endpoints.Single(e => e.RoutePattern.RawText == route).Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        Policy("Account/Login").ShouldBe(CoworkeeRateLimitOptions.Auth);
        Policy("Account/ForgotPassword").ShouldBe(CoworkeeRateLimitOptions.Auth);
        Policy("/connect/token").ShouldBeNull();
    }
}
