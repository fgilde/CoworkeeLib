using System.Collections.Concurrent;
using System.Diagnostics;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace Coworkee.Bff.Tests;

/// <summary>Token links reach the API through the BFF's forwarder: the token stays out of the BFF's logs (scopes included) and activities.</summary>
public sealed class RedactionTests : IAsyncLifetime
{
    private const string Token = "s3cr3t-t0k3n";
    private readonly CapturedLogs _logs = new();
    private WebApplication _api = null!;
    private WebApplication _bff = null!;

    public async ValueTask InitializeAsync()
    {
        var api = WebApplication.CreateBuilder();
        api.WebHost.UseTestServer();
        _api = api.Build();
        _api.MapGet("/api/v1/files/token/{token}/{name}", (string token) => Results.Text(token == Token ? "pdf" : "?"));
        await _api.StartAsync();

        var bff = WebApplication.CreateBuilder();
        bff.WebHost.UseTestServer();
        bff.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
        });
        bff.Logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs);
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

    [Fact]
    public async Task A_token_link_through_the_bff_leaves_no_token_in_logs_or_activities()
    {
        var ct = TestContext.Current.CancellationToken;
        var activities = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Microsoft.AspNetCore" or "Yarp.ReverseProxy" or "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            // as the OpenTelemetry instrumentation does, from the raw request
            ActivityStarted = activity => activity.SetTag("url.full", $"http://bff.test/api/v1/files/token/{Token}/a.pdf"),
            ActivityStopped = activity => activities.Enqueue($"{activity.Source.Name}: {activity.DisplayName} {string.Join(" ", activity.Tags.Select(t => t.Value))}"),
        };
        ActivitySource.AddActivityListener(listener);

        var response = await _bff.GetTestClient().GetStringAsync($"/api/v1/files/token/{Token}/a.pdf", ct);

        response.ShouldBe("pdf");
        _logs.Lines.ShouldContain(line => line.Contains("/api/v1/files/token/***/a.pdf"));
        _logs.Lines.ShouldContain(line => line.StartsWith("Yarp.", StringComparison.Ordinal));
        _logs.Lines.ShouldContain(line => line.Contains("RequestPath=/api/v1/files/token/***/a.pdf"));
        _logs.Lines.ShouldNotContain(line => line.Contains(Token));
        activities.ShouldContain(a => a.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        activities.ShouldNotContain(a => a.Contains(Token));
    }

    private sealed class TestServerClients(TestServer server) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(server.CreateHandler());
    }
}
