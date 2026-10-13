using System.Net;
using System.Text;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Coworkee.Settings.Tests;

public sealed class ServiceDirectoryTests
{
    private const string Apps = """
        [
          { "name": "billing", "kind": "hosted", "healthy": true, "urls": ["http://localhost:5001", "https://localhost:5002"] },
          { "name": "shop", "kind": "stack", "healthy": false, "urls": [] }
        ]
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AspireUI_apps_are_mapped()
    {
        using var provider = Build(new FakeHandler(_ => Json(Apps)), aspire: o => o.Url = "http://aspire.test");

        var services = await provider.GetRequiredService<IServiceDirectory>().GetAllAsync(Ct);

        services.Select(s => (s.Name, s.Title, s.Url, s.Health, s.Source, Urls: string.Join(" ", s.Urls!))).ShouldBe(
        [
            ("billing", "billing", "http://localhost:5001", ServiceHealth.Healthy, "aspireui", "http://localhost:5001 https://localhost:5002"),
            ("shop", "shop", "", ServiceHealth.Unhealthy, "aspireui", ""),
        ]);
    }

    [Fact]
    public async Task The_api_key_goes_into_the_header_only_when_set()
    {
        var withKey = new FakeHandler(_ => Json(Apps));
        var withoutKey = new FakeHandler(_ => Json(Apps));

        using (var provider = Build(withKey, aspire: o => (o.Url, o.ApiKey) = ("http://aspire.test", "s3cret")))
        {
            await provider.GetRequiredService<IServiceDirectory>().GetAllAsync(Ct);
        }

        using (var provider = Build(withoutKey, aspire: o => o.Url = "http://aspire.test"))
        {
            await provider.GetRequiredService<IServiceDirectory>().GetAllAsync(Ct);
        }

        withKey.Requests.ShouldHaveSingleItem().Headers.GetValues("X-Api-Key").ShouldBe(["s3cret"]);
        withoutKey.Requests.ShouldHaveSingleItem().Headers.Contains("X-Api-Key").ShouldBeFalse();
        withKey.Requests[0].RequestUri!.AbsoluteUri.ShouldBe("http://aspire.test/api/retrieve/apps");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refusing_AspireUI_gives_no_apps_and_the_key_stays_out_of_the_log(HttpStatusCode status)
    {
        var log = new ListLoggerProvider();
        using var provider = Build(new FakeHandler(_ => new HttpResponseMessage(status)), log, o => (o.Url, o.ApiKey) = ("http://aspire.test", "s3cret"));

        var services = await provider.GetRequiredService<IServiceDirectory>().GetAllAsync(Ct);

        services.ShouldBeEmpty();
        log.Messages.ShouldHaveSingleItem().ShouldNotContain("s3cret");
    }

    [Fact]
    public async Task An_unreachable_AspireUI_gives_no_apps_after_the_timeout_and_keeps_the_configured_services()
    {
        var handler = new FakeHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/retrieve/apps")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = Build(handler, aspire: o => o.Url = "http://aspire.test", services: o => o["self"] = new CoworkeeServiceOptions { Url = "http://self.test" });

        var started = DateTime.UtcNow;
        var services = await provider.GetRequiredService<IServiceDirectory>().GetAllAsync(Ct);

        services.Select(s => s.Name).ShouldBe(["self"]);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Without_an_AspireUI_address_nothing_is_requested()
    {
        var handler = new FakeHandler(_ => Json(Apps));
        using var provider = Build(handler);

        (await provider.GetRequiredService<IServiceDirectory>().GetAllAsync(Ct)).ShouldBeEmpty();

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_AspireUI_list_is_requested_once_per_cache_period()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(100, token);
            return Json(Apps);
        });
        using var provider = Build(handler, aspire: o => o.Url = "http://aspire.test");
        var directory = provider.GetRequiredService<IServiceDirectory>();

        await Task.WhenAll(directory.GetAllAsync(Ct), directory.GetAllAsync(Ct), directory.GetAllAsync(Ct));
        await directory.GetAllAsync(Ct);

        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Both_sources_are_listed_and_a_configured_service_wins_when_resolving()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/api/retrieve/apps" ? Json(Apps) : new HttpResponseMessage(HttpStatusCode.OK));
        using var provider = Build(
            handler,
            aspire: o => o.Url = "http://aspire.test",
            services: o =>
            {
                o["Billing"] = new CoworkeeServiceOptions { Url = "http://configured.test", Title = "Configured billing" };
                o["mail"] = new CoworkeeServiceOptions { Url = "http://mail.test" };
            });
        var directory = provider.GetRequiredService<IServiceDirectory>();

        var all = await directory.GetAllAsync(Ct);
        var billing = await directory.ResolveAsync("BILLING", Ct);
        var shop = await directory.ResolveAsync("Shop", Ct);
        var unknown = await directory.ResolveAsync("nope", Ct);

        all.Select(s => (s.Name, s.Source)).ShouldBe([("Billing", "app"), ("mail", "app"), ("billing", "aspireui"), ("shop", "aspireui")]);
        (billing!.Url, billing.Source).ShouldBe(("http://configured.test", "app"));
        (shop!.Url, shop.Source).ShouldBe(("", "aspireui"));
        unknown.ShouldBeNull();
    }

    [Fact]
    public async Task Resolving_does_not_probe_the_health_of_configured_services()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var provider = Build(handler, services: o => o["self"] = new CoworkeeServiceOptions { Url = "http://self.test" });

        (await provider.GetRequiredService<IServiceDirectory>().ResolveAsync("self", Ct))!.Url.ShouldBe("http://self.test");

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_source_can_be_registered()
    {
        using var provider = Build(new FakeHandler(_ => Json(Apps)), configure: s => s.AddSingleton<IServiceSource, OwnSource>());

        var other = await provider.GetRequiredService<IServiceDirectory>().ResolveAsync("own", Ct);

        other!.Url.ShouldBe("http://own.test");
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static ServiceProvider Build(
        FakeHandler handler, ILoggerProvider? log = null, Action<CoworkeeAspireUIOptions>? aspire = null, Action<CoworkeeServicesOptions>? services = null, Action<IServiceCollection>? configure = null)
    {
        var collection = new ServiceCollection();
        collection.AddHybridCache();
        collection.AddLogging(logging => logging.AddProvider(log ?? new ListLoggerProvider()));
        collection.Configure<CoworkeeAspireUIOptions>(o => aspire?.Invoke(o));
        collection.Configure<CoworkeeServicesOptions>(o => services?.Invoke(o));
        collection.AddSingleton<IHttpClientFactory>(new FakeFactory(handler));
        collection.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceSource, ConfiguredServiceSource>());
        collection.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceSource, AspireUIServiceSource>());
        collection.TryAddSingleton<IServiceDirectory, ServiceDirectory>();
        configure?.Invoke(collection);
        return collection.BuildServiceProvider();
    }

    private sealed class OwnSource : IServiceSource
    {
        public string Name => "own";

        public Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServiceDto>>([new ServiceDto("own", "own", "http://own.test", ServiceHealth.Healthy, "own")]);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return respond(request, cancellationToken);
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ListLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                {
                    messages.Add(formatter(state, exception) + exception);
                }
            }
        }
    }
}
