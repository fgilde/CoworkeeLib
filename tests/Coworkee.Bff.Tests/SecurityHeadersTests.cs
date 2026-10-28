using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Coworkee.Bff.Tests;

public sealed class SecurityHeadersTests
{
    [Fact]
    public void The_policy_lets_blazor_and_the_file_previews_run()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy(new BffOptions(), development: false);

        policy.ShouldContain("script-src 'self' 'wasm-unsafe-eval' 'unsafe-eval' https://cdn.jsdelivr.net https://esm.sh https://unpkg.com;");
        policy.ShouldContain("style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://esm.sh https://unpkg.com;");
        policy.ShouldContain("connect-src 'self' https://cdn.jsdelivr.net https://esm.sh https://unpkg.com;");
        policy.ShouldContain("img-src 'self' data: blob: https:;");
        policy.ShouldContain("object-src 'self' data: blob:");
        policy.ShouldContain("frame-src 'self' data: blob:");
        policy.ShouldEndWith("frame-ancestors 'self'");
    }

    [Fact]
    public void Development_lets_hot_reload_connect() =>
        SecurityHeaders.ContentSecurityPolicy(new BffOptions(), development: true).ShouldContain("connect-src 'self' http://localhost:* ws://localhost:* wss://localhost:*");

    [Fact]
    public void Without_the_file_viewer_cdns_no_third_party_script_is_allowed()
    {
        var options = new BffOptions();
        options.SecurityHeaders.FileViewerCdns.Clear();

        var policy = SecurityHeaders.ContentSecurityPolicy(options, development: false);

        policy.ShouldContain("script-src 'self' 'wasm-unsafe-eval' 'unsafe-eval';");
        policy.ShouldContain("connect-src 'self';");
    }

    [Fact]
    public void Extra_sources_join_their_directive_or_add_one()
    {
        var options = new BffOptions();
        options.SecurityHeaders.Sources["script-src"] = ["https://apis.google.com"];
        options.SecurityHeaders.Sources["manifest-src"] = ["'self'"];

        var policy = SecurityHeaders.ContentSecurityPolicy(options, development: false);

        policy.ShouldContain("script-src 'self' 'wasm-unsafe-eval' 'unsafe-eval' https://cdn.jsdelivr.net https://esm.sh https://unpkg.com https://apis.google.com;");
        policy.ShouldContain("manifest-src 'self'");
    }

    [Fact]
    public void A_replaced_policy_is_sent_as_is()
    {
        var options = new BffOptions { SecurityHeaders = { ContentSecurityPolicy = "default-src 'none'" } };

        SecurityHeaders.ContentSecurityPolicy(options, development: false).ShouldBe("default-src 'none'");
    }

    [Fact]
    public async Task Every_answer_carries_one_policy_with_the_frame_ancestors()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
            ["Coworkee:Bff:FrameAncestors:0"] = "https://desk.test/",
        });
        builder.AddCoworkeeBff();
        await using var app = builder.Build();
        app.UseCoworkeeSecurityHeaders(o => o.ReferrerPolicy = "no-referrer");
        app.MapCoworkeeBff();
        app.MapGet("/own", (HttpContext context) =>
        {
            context.Response.Headers.ContentSecurityPolicy = "sandbox";
            return "own";
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/bff/user", TestContext.Current.CancellationToken);
        var own = await client.GetAsync("/own", TestContext.Current.CancellationToken);

        response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldEndWith("frame-ancestors 'self' https://desk.test");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        own.Headers.GetValues("Content-Security-Policy").ShouldBe(["sandbox"]);
    }

    [Fact]
    public async Task An_answer_that_brings_its_own_nosniff_still_carries_it_once()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
        });
        builder.AddCoworkeeBff();
        await using var app = builder.Build();
        app.UseCoworkeeSecurityHeaders();
        // as the reverse proxy does with the headers of the API's answer
        app.MapGet("/forwarded", (HttpContext context) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return "forwarded";
        });
        await app.StartAsync(TestContext.Current.CancellationToken);

        var response = await app.GetTestClient().GetAsync("/forwarded", TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }
}
