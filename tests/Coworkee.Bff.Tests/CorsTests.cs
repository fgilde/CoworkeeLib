using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Coworkee.Bff.Tests;

/// <summary>The API allows a single-page app's origin, even with credentials on the hub; through the BFF, where the cookie is the credential, nobody may.</summary>
public sealed class CorsTests : IAsyncLifetime
{
    private const string Spa = "https://spa.test";
    private WebApplication _api = null!;
    private WebApplication _bff = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var api = WebApplication.CreateBuilder();
        api.WebHost.UseTestServer();
        api.Services.AddCors(cors => cors.AddDefaultPolicy(p => p.WithOrigins(Spa).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
        _api = api.Build();
        _api.UseCors();
        _api.MapMethods("/hubs/realtime/negotiate", ["POST"], () => "{}");
        _api.MapGet("/api/v1/identity/me", () => "{}");
        await _api.StartAsync();

        var bff = WebApplication.CreateBuilder();
        bff.WebHost.UseTestServer();
        bff.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
            ["Coworkee:Bff:ForwardedPrefixes:0"] = "/hubs",
        });
        bff.AddCoworkeeBff();
        bff.Services.AddSingleton<IForwarderHttpClientFactory>(new TestServerClients(_api.GetTestServer()));
        _bff = bff.Build();
        _bff.MapCoworkeeBff();
        await _bff.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _bff.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Theory]
    [InlineData("/hubs/realtime/negotiate", "POST")]
    [InlineData("/api/v1/identity/me", "GET")]
    public async Task A_credentialed_preflight_through_the_bff_gets_no_cors_headers(string path, string method)
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, path);
        preflight.Headers.Add("Origin", Spa);
        preflight.Headers.Add("Access-Control-Request-Method", method);
        preflight.Headers.Add("Access-Control-Request-Headers", "x-csrf");

        var direct = await _api.GetTestClient().SendAsync(Copy(preflight), Ct);
        direct.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(["true"]);

        var response = await _bff.GetTestClient().SendAsync(preflight, Ct);

        response.Headers.Where(h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_answer_forwarded_to_another_origin_carries_no_cors_headers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        request.Headers.Add("Origin", Spa);

        var response = await _bff.GetTestClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Where(h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }

    private static HttpRequestMessage Copy(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return copy;
    }

    private sealed class TestServerClients(TestServer server) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(server.CreateHandler());
    }
}
