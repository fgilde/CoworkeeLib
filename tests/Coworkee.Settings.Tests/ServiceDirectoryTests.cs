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

        var services = (await Dir(provider).GetAllAsync(Ct)).Services;

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
            await Dir(provider).GetAllAsync(Ct);
        }

        using (var provider = Build(withoutKey, aspire: o => o.Url = "http://aspire.test"))
        {
            await Dir(provider).GetAllAsync(Ct);
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

        var result = await Dir(provider).GetAllAsync(Ct);

        result.Services.ShouldBeEmpty();
        result.UnavailableSources.ShouldBe(["aspireui"]);
        log.Messages.ShouldNotBeEmpty();
        log.Messages.ShouldAllBe(m => !m.Contains("s3cret"));
    }

    [Fact]
    public async Task A_failed_request_is_cached_like_an_answer()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var provider = Build(handler, aspire: o => o.Url = "http://aspire.test");

        await Dir(provider).GetAllAsync(Ct);
        await Dir(provider).GetAllAsync(Ct);

        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_answer_that_is_not_the_app_list_marks_the_AspireUI_unavailable()
    {
        var html = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>", Encoding.UTF8, "text/html") });
        var broken = new FakeHandler(_ => Json("{ not json"));
        using var htmlProvider = Build(html, aspire: o => o.Url = "http://aspire.test");
        using var brokenProvider = Build(broken, aspire: o => o.Url = "http://aspire.test");

        var htmlResult = await Dir(htmlProvider).GetAllAsync(Ct);
        var brokenResult = await Dir(brokenProvider).GetAllAsync(Ct);

        htmlResult.Services.ShouldBeEmpty();
        htmlResult.UnavailableSources.ShouldBe(["aspireui"]);
        brokenResult.UnavailableSources.ShouldBe(["aspireui"]);
    }

    [Fact]
    public async Task Null_and_nameless_entries_of_the_app_list_are_skipped()
    {
        using var provider = Build(new FakeHandler(_ => Json("""[null, {"healthy": true}, {"name": "ok", "healthy": true, "urls": [null, "", "http://ok.test"]}]""")), aspire: o => o.Url = "http://aspire.test");

        var result = await Dir(provider).GetAllAsync(Ct);

        result.Services.Select(s => (s.Name, s.Url, string.Join(" ", s.Urls!))).ShouldBe([("ok", "http://ok.test", "http://ok.test")]);
        result.UnavailableSources.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_path_in_the_AspireUI_address_is_kept()
    {
        var handler = new FakeHandler(_ => Json(Apps));
        using var provider = Build(handler, aspire: o => o.Url = "http://aspire.test/ui/");

        await Dir(provider).GetAllAsync(Ct);

        handler.Requests.ShouldHaveSingleItem().RequestUri!.AbsoluteUri.ShouldBe("http://aspire.test/ui/api/retrieve/apps");
    }

    [Fact]
    public async Task A_failing_source_is_left_out_and_named_while_the_others_still_answer()
    {
        using var provider = Build(new FakeHandler(_ => Json(Apps)), aspire: o => o.Url = "http://aspire.test", configure: s => s.AddSingleton<IServiceSource, ThrowingSource>());
        var directory = Dir(provider);

        var all = await directory.GetAllAsync(Ct);
        var shop = await directory.ResolveAsync("shop", Ct);

        all.Services.Select(s => s.Name).ShouldBe(["billing", "shop"]);
        all.UnavailableSources.ShouldBe(["broken"]);
        shop!.Source.ShouldBe("aspireui");
    }

    [Fact]
    public async Task A_cancelled_caller_is_not_taken_for_a_failing_source()
    {
        using var provider = Build(new FakeHandler(_ => Json(Apps)), configure: s => s.AddSingleton<IServiceSource, ThrowingSource>());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Dir(provider).GetAllAsync(cancelled.Token));
    }

    [Fact]
    public async Task A_scoped_source_can_be_registered()
    {
        using var provider = Build(new FakeHandler(_ => Json(Apps)), configure: s => s.AddScoped<ScopedMarker>().AddScoped<IServiceSource, ScopedSource>());

        var all = await Dir(provider).GetAllAsync(Ct);

        all.Services.Select(s => s.Name).ShouldBe(["scoped"]);
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
        using var provider = Build(handler, aspire: o => o.Url = "http://aspire.test", services: o => o["self"] = new CoworkeeServiceOptions { Url = "http://self.test" }, time: new FastTime());

        var result = await Dir(provider).GetAllAsync(Ct);

        result.Services.Select(s => s.Name).ShouldBe(["self"]);
        result.UnavailableSources.ShouldBe(["aspireui"]);
    }

    [Fact]
    public async Task A_service_that_never_answers_is_unreachable_while_the_app_source_stays_available()
    {
        var handler = new FakeHandler(async (request, token) =>
        {
            if (request.RequestUri!.Host == "slow.test")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = Build(
            handler,
            services: o =>
            {
                o["fast"] = new CoworkeeServiceOptions { Url = "http://fast.test" };
                o["slow"] = new CoworkeeServiceOptions { Url = "http://slow.test" };
            },
            time: new FastTime());

        var result = await Dir(provider).GetAllAsync(Ct);

        result.UnavailableSources.ShouldBeEmpty();
        result.Services.Select(s => (s.Name, s.Health)).ShouldBe([("fast", ServiceHealth.Healthy), ("slow", ServiceHealth.Unreachable)]);
    }

    [Fact]
    public void The_directory_guard_is_longer_than_the_timeout_of_the_built_in_sources()
    {
        ServiceSourceTimeouts.Guard.ShouldBeGreaterThan(ServiceSourceTimeouts.Request);
    }

    [Fact]
    public async Task A_source_that_ignores_cancellation_is_unavailable_after_the_guard_time()
    {
        using var provider = Build(new FakeHandler(_ => Json(Apps)), aspire: o => o.Url = "http://aspire.test", configure: s => s.AddSingleton<IServiceSource, HangingSource>(), time: new FastTime());
        var directory = Dir(provider);

        var all = await directory.GetAllAsync(Ct);
        var hung = await directory.ResolveAsync("hanging:x", Ct);

        all.Services.Select(s => s.Name).ShouldBe(["billing", "shop"]);
        all.UnavailableSources.ShouldBe(["hanging"]);
        hung.ShouldBeNull();
    }

    [Fact]
    public async Task Every_entry_carries_the_name_of_its_source()
    {
        using var provider = Build(new FakeHandler(_ => Json(Apps)), configure: s => s.AddSingleton<IServiceSource, MislabelingSource>());
        var directory = Dir(provider);

        var all = await directory.GetAllAsync(Ct);
        var resolved = await directory.ResolveAsync("mislabeled", Ct);

        all.Services.ShouldHaveSingleItem().Source.ShouldBe("mislabeling");
        resolved!.Source.ShouldBe("mislabeling");
    }

    [Fact]
    public async Task A_qualified_name_asks_that_source_only()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/api/retrieve/apps" ? Json(Apps) : new HttpResponseMessage(HttpStatusCode.OK));
        using var provider = Build(
            handler,
            aspire: o => o.Url = "http://aspire.test",
            services: o => o["billing"] = new CoworkeeServiceOptions { Url = "http://configured.test" });
        var directory = Dir(provider);

        var plain = await directory.ResolveAsync("billing", Ct);
        var app = await directory.ResolveAsync("app:billing", Ct);
        var aspire = await directory.ResolveAsync("AspireUI:Billing", Ct);

        (plain!.Source, plain.Url).ShouldBe(("app", "http://configured.test"));
        (app!.Source, app.Url).ShouldBe(("app", "http://configured.test"));
        (aspire!.Source, aspire.Url).ShouldBe(("aspireui", "http://localhost:5001"));
        (await directory.ResolveAsync("aspireui:mail", Ct)).ShouldBeNull();
        (await directory.ResolveAsync("nope:billing", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_failure_the_source_already_logged_is_not_logged_again_and_the_user_info_stays_out_of_the_log()
    {
        var log = new ListLoggerProvider();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var provider = Build(handler, log, o => o.Url = "http://name:pw0rd@aspire.test");

        await Dir(provider).GetAllAsync(Ct);

        log.Messages.ShouldHaveSingleItem().ShouldNotContain("pw0rd");
        handler.Requests.ShouldHaveSingleItem().RequestUri!.UserInfo.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_an_AspireUI_address_nothing_is_requested()
    {
        var handler = new FakeHandler(_ => Json(Apps));
        using var provider = Build(handler);

        var result = await Dir(provider).GetAllAsync(Ct);

        result.Services.ShouldBeEmpty();
        result.UnavailableSources.ShouldBeEmpty();
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
        var directory = Dir(provider);

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
        var directory = Dir(provider);

        var all = (await directory.GetAllAsync(Ct)).Services;
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

        var self = await Dir(provider).ResolveAsync("self", Ct);

        (self!.Url, self.Health).ShouldBe(("http://self.test", ServiceHealth.Unknown));

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_source_can_be_registered()
    {
        using var provider = Build(new FakeHandler(_ => Json(Apps)), configure: s => s.AddSingleton<IServiceSource, OwnSource>());

        var other = await Dir(provider).ResolveAsync("own", Ct);

        other!.Url.ShouldBe("http://own.test");
    }

    private static IServiceDirectory Dir(ServiceProvider provider) => provider.CreateScope().ServiceProvider.GetRequiredService<IServiceDirectory>();

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static ServiceProvider Build(
        FakeHandler handler, ILoggerProvider? log = null, Action<CoworkeeAspireUIOptions>? aspire = null, Action<CoworkeeServicesOptions>? services = null, Action<IServiceCollection>? configure = null, TimeProvider? time = null)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton(time ?? TimeProvider.System);
        collection.AddHybridCache();
        collection.AddLogging(logging => logging.AddProvider(log ?? new ListLoggerProvider()));
        collection.Configure<CoworkeeAspireUIOptions>(o => aspire?.Invoke(o));
        collection.Configure<CoworkeeServicesOptions>(o => services?.Invoke(o));
        collection.AddSingleton<IHttpClientFactory>(new FakeFactory(handler));
        collection.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceSource, ConfiguredServiceSource>());
        collection.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceSource, AspireUIServiceSource>());
        collection.TryAddScoped<IServiceDirectory, ServiceDirectory>();
        configure?.Invoke(collection);
        return collection.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FastTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            base.CreateTimer(callback, state, dueTime / 100, period);
    }

    private sealed class OwnSource : IServiceSource
    {
        public string Name => "own";

        public Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServiceDto>>([new ServiceDto("own", "own", "http://own.test", ServiceHealth.Healthy, "own")]);
    }

    private sealed class HangingSource : IServiceSource
    {
        public string Name => "hanging";

        public async Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken) =>
            await new TaskCompletionSource<IReadOnlyList<ServiceDto>>().Task;
    }

    private sealed class MislabelingSource : IServiceSource
    {
        public string Name => "mislabeling";

        public Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServiceDto>>([new ServiceDto("mislabeled", "mislabeled", "http://m.test", ServiceHealth.Healthy, "other")]);
    }

    private sealed class ThrowingSource : IServiceSource
    {
        public string Name => "broken";

        public async Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class ScopedMarker;

    private sealed class ScopedSource(ScopedMarker marker) : IServiceSource
    {
        public string Name => marker.GetType().Name;

        public Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServiceDto>>([new ServiceDto("scoped", "scoped", "http://scoped.test", ServiceHealth.Healthy, "scoped")]);
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
