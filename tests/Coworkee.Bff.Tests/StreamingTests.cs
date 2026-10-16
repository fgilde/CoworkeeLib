using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Coworkee.Bff.Tests;

/// <summary>The BFF passes streamed answers (the global search, the assistant) on line by line instead of buffering them.</summary>
public sealed class StreamingTests : IAsyncLifetime
{
    private readonly TaskCompletionSource _second = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WebApplication _api = null!;
    private WebApplication _bff = null!;

    public async ValueTask InitializeAsync()
    {
        var api = WebApplication.CreateBuilder();
        api.WebHost.UseTestServer();
        _api = api.Build();
        _api.MapGet("/api/v1/search/stream", async (HttpContext context) =>
        {
            context.Response.ContentType = "application/x-ndjson";
            await context.Response.WriteAsync("{\"provider\":\"first\"}\n");
            await context.Response.Body.FlushAsync();
            await _second.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await context.Response.WriteAsync("{\"provider\":\"second\"}\n");
        });
        await _api.StartAsync();

        var bff = WebApplication.CreateBuilder();
        bff.WebHost.UseTestServer();
        bff.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
        });
        bff.AddCoworkeeBff();
        bff.Services.AddSingleton<IForwarderHttpClientFactory>(new TestServerClients(_api.GetTestServer()));
        _bff = bff.Build();
        _bff.MapCoworkeeBff();
        await _bff.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _second.TrySetResult();
        await _bff.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task A_streamed_line_reaches_the_browser_before_the_answer_ends()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/search/stream?q=x");
        request.Headers.Add("X-CSRF", "1");

        using var response = await _bff.GetTestClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));

        (await reader.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), ct)).ShouldBe("{\"provider\":\"first\"}");
        _second.SetResult();
        (await reader.ReadLineAsync(ct)).ShouldBe("{\"provider\":\"second\"}");
    }

    private sealed class TestServerClients(TestServer server) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(server.CreateHandler());
    }
}
