using System.Net;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Coworkee.AspNetCore.Tests;

public sealed class CorsTests
{
    private const string Spa = "https://spa.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_configured_origin_may_call_without_credentials_and_others_get_no_cors_headers()
    {
        await using var app = await StartAsync(new() { ["Coworkee:Cors:Origins:0"] = Spa + "/app/" });

        var preflight = await PreflightAsync(app, "/values", Spa);
        preflight.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        preflight.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Spa]);
        preflight.Headers.GetValues("Access-Control-Allow-Headers").Single().ShouldContain("authorization", Case.Insensitive);
        preflight.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();

        var call = await SendAsync(app, HttpMethod.Get, "/values", Spa);
        call.StatusCode.ShouldBe(HttpStatusCode.OK);
        call.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Spa]);
        call.Headers.GetValues("Access-Control-Expose-Headers").ShouldContain("Content-Disposition");

        (await PreflightAsync(app, "/values", "https://evil.test")).Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        (await PreflightAsync(app, "/values", "http://spa.test")).Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        (await SendAsync(app, HttpMethod.Get, "/values", "https://evil.test")).Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Only_endpoints_that_opt_in_allow_credentials()
    {
        await using var app = await StartAsync(new() { ["Coworkee:Cors:Origins:0"] = Spa });

        var hub = await PreflightAsync(app, "/hub/negotiate", Spa, HttpMethod.Post);
        hub.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Spa]);
        hub.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(["true"]);
        (await PreflightAsync(app, "/hub/negotiate", "https://evil.test", HttpMethod.Post)).Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public async Task Without_origins_nothing_cross_origin_is_allowed_and_same_origin_calls_work()
    {
        await using var app = await StartAsync([]);

        (await PreflightAsync(app, "/values", Spa)).Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        (await PreflightAsync(app, "/hub/negotiate", Spa, HttpMethod.Post)).Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        (await app.GetTestClient().GetAsync("/values", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("spa.test")]
    [InlineData("ftp://spa.test")]
    public async Task A_wildcard_or_no_http_origin_stops_the_start(string origin) =>
        (await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(new() { ["Coworkee:Cors:Origins:0"] = origin }))).Message.ShouldContain("Coworkee:Cors:Origins");

    private static Task<HttpResponseMessage> PreflightAsync(WebApplication app, string path, string origin, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", (method ?? HttpMethod.Get).Method);
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return app.GetTestClient().SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> SendAsync(WebApplication app, HttpMethod method, string path, string origin)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", origin);
        return app.GetTestClient().SendAsync(request, Ct);
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddCoworkee<CorsModule>();
        var app = builder.Build();
        app.UseCoworkee();
        await app.StartAsync(Ct);
        return app;
    }

    private sealed class CorsModule : CoworkeeModule, IWebModule
    {
        public void ConfigureApplication(WebApplication app)
        {
            app.MapGet("/values", () => Results.Ok(42));
            app.MapPost("/hub/negotiate", () => Results.Ok()).RequireCors(CoworkeeCors.WithCredentials);
        }
    }
}
