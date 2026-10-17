using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Coworkee.Bff.Tests;

/// <summary>Kestrel's body limit (here 1 MB) does not stop uploads to the API: the API checks its own limits per endpoint.</summary>
public sealed class UploadLimitTests : IAsyncDisposable
{
    private const int Kilobyte = 1024;
    private WebApplication? _api;
    private WebApplication? _bff;

    [Fact]
    public async Task An_upload_larger_than_kestrels_limit_reaches_the_api()
    {
        var response = await UploadAsync(2 * Kilobyte * Kilobyte, max: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe((2 * Kilobyte * Kilobyte).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_configured_limit_refuses_larger_uploads() =>
        (await UploadAsync(64 * Kilobyte, max: 16 * Kilobyte)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

    public async ValueTask DisposeAsync()
    {
        if (_bff is not null)
        {
            await _bff.DisposeAsync();
        }

        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }

    private async Task<HttpResponseMessage> UploadAsync(int size, long? max)
    {
        var ct = TestContext.Current.CancellationToken;
        var api = WebApplication.CreateBuilder();
        api.WebHost.UseTestServer();
        _api = api.Build();
        _api.MapPost("/api/v1/files", async (HttpContext context) =>
        {
            var length = 0L;
            var buffer = new byte[81920];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                length += read;
            }

            return length;
        });
        await _api.StartAsync(ct);

        var bff = WebApplication.CreateBuilder();
        bff.WebHost.UseKestrel(k => k.Limits.MaxRequestBodySize = Kilobyte * Kilobyte).UseUrls("http://127.0.0.1:0");
        bff.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
            ["Coworkee:Bff:MaxRequestBodySize"] = max?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        bff.AddCoworkeeBff();
        bff.Services.AddSingleton<IForwarderHttpClientFactory>(new TestServerClients(_api.GetTestServer()));
        _bff = bff.Build();
        _bff.MapCoworkeeBff();
        await _bff.StartAsync(ct);

        var address = _bff.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files") { Content = new ByteArrayContent(new byte[size]) };
        request.Headers.Add("X-CSRF", "1");
        return await client.SendAsync(request, ct);
    }

    private sealed class TestServerClients(TestServer server) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(server.CreateHandler());
    }
}
