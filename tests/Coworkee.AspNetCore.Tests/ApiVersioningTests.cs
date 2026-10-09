using System.Net;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.AspNetCore.Tests;

public sealed class ApiVersioningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_version_has_its_routes_and_its_own_document_and_v1_keeps_the_unversioned_endpoints()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddCoworkee<VersionedModule>();
        await using var app = builder.Build();
        app.UseCoworkee();
        await app.StartAsync(Ct);
        var client = app.GetTestClient();

        (await client.GetStringAsync("/api/v1/things", Ct)).ShouldBe("one");
        (await client.GetStringAsync("/api/v2/things", Ct)).ShouldBe("two");
        (await client.GetStringAsync("/plain", Ct)).ShouldBe("plain");
        (await client.GetAsync("/api/v3/things", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var v1 = await client.GetStringAsync("/openapi/v1.json", Ct);
        v1.ShouldContain("\"/api/v1/things\"");
        v1.ShouldContain("\"/plain\"");
        v1.ShouldNotContain("/api/v2/");
        var v2 = await client.GetStringAsync("/openapi/v2.json", Ct);
        v2.ShouldContain("\"/api/v2/things\"");
        v2.ShouldNotContain("/api/v1/");
        v2.ShouldNotContain("\"/plain\"");
        v2.ShouldContain("\"bearerFormat\": \"JWT\"");
    }

    private sealed class VersionedModule : CoworkeeModule, IWebModule
    {
        public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddCoworkeeApiVersion(2);

        public void ConfigureApplication(WebApplication app)
        {
            app.MapCoworkeeApi("/api/v1/things").MapGet("/", () => "one");
            app.MapCoworkeeApi("/api/v2/things").MapGet("/", () => "two");
            app.MapGet("/plain", () => "plain");
        }
    }
}
