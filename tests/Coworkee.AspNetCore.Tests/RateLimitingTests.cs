using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.Core.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Coworkee.AspNetCore.Tests;

public sealed class RateLimitingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_policy_answers_429_with_problem_details_once_the_window_is_used_up()
    {
        await using var app = await StartAsync(new() { ["Coworkee:RateLimiting:Policies:ai:PermitLimit"] = "2" });
        var client = app.GetTestClient();

        (await client.GetAsync("/limited", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/limited", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await client.GetAsync("/limited", Ct);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        (await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("rate_limited");
        (await client.GetAsync("/free", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_global_limit_is_off_unless_configured_and_everything_is_off_when_disabled()
    {
        await using (var app = await StartAsync(new() { ["Coworkee:RateLimiting:Global:PermitLimit"] = "1" }))
        {
            (await app.GetTestClient().GetAsync("/free", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await app.GetTestClient().GetAsync("/free", Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        }

        await using var disabled = await StartAsync(new() { ["Coworkee:RateLimiting:Enabled"] = "false", ["Coworkee:RateLimiting:Policies:ai:PermitLimit"] = "1" });
        for (var i = 0; i < 3; i++)
        {
            (await disabled.GetTestClient().GetAsync("/limited", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddCoworkee<LimitedModule>();
        var app = builder.Build();
        app.UseCoworkee();
        await app.StartAsync(Ct);
        return app;
    }

    private sealed class LimitedModule : CoworkeeModule, IWebModule
    {
        public void ConfigureApplication(WebApplication app)
        {
            app.MapGet("/limited", () => Results.Ok()).RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Ai);
            app.MapGet("/free", () => Results.Ok());
        }
    }
}
