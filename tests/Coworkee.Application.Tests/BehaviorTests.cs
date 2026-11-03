using Coworkee.Application.Caching;
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Coworkee.Application.Tests;

public sealed class BehaviorTests
{
    private static readonly AsyncLocal<string?> Ambient = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_open_behavior_wraps_every_request_and_a_closed_one_only_its_request()
    {
        var calls = new List<string>();
        var dispatcher = Build(services =>
        {
            services.AddSingleton(calls);
            services.AddSingleton(new Counter());
            services.AddRequestBehavior(typeof(Tracing<,>));
            services.AddRequestBehavior<Greet, string, GreetOnly>();
        });

        (await dispatcher.SendAsync(new Greet("Ada"), Ct)).ShouldBe("Hello Ada!");
        await dispatcher.SendAsync(new Count(), Ct);

        calls.ShouldBe(["trace Greet", "greet only", "trace Count"]);
    }

    [Fact]
    public async Task Cached_queries_answer_from_the_cache_per_tenant_until_a_command_invalidates_them()
    {
        var counter = new Counter();
        var tenant = Guid.CreateVersion7();
        var dispatcher = Build(services => services.AddSingleton(counter), tenant);

        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(1);
        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(1);
        (await Build(services => services.AddSingleton(counter), Guid.CreateVersion7(), dispatcher.Cache).SendAsync(new Count(), Ct)).Value.ShouldBe(2);

        (await dispatcher.SendAsync(new Bump(), Ct)).IsSuccess.ShouldBeTrue();

        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(3);
    }

    [Fact]
    public async Task A_command_in_any_organisation_invalidates_global_entries_and_tenant_entries_only_its_own()
    {
        var counter = new Counter();
        var a = Build(services => services.AddSingleton(counter), Guid.CreateVersion7());
        var b = Build(services => services.AddSingleton(counter), Guid.CreateVersion7(), a.Cache);

        (await a.SendAsync(new GlobalCount(), Ct)).Value.ShouldBe(1);
        (await b.SendAsync(new GlobalCount(), Ct)).Value.ShouldBe(1);
        (await a.SendAsync(new Count(), Ct)).Value.ShouldBe(2);

        (await b.SendAsync(new Bump(), Ct)).IsSuccess.ShouldBeTrue();

        (await a.SendAsync(new GlobalCount(), Ct)).Value.ShouldBe(3);
        (await a.SendAsync(new Count(), Ct)).Value.ShouldBe(2);
    }

    [Fact]
    public async Task Cached_query_handlers_run_in_the_async_context_of_the_caller()
    {
        var dispatcher = Build(_ => { });
        Ambient.Value = "caller";

        (await dispatcher.SendAsync(new ReadAmbient(), Ct)).Value.ShouldBe("caller");
    }

    [Fact]
    public async Task A_caller_that_aborts_a_shared_load_leaves_the_others_their_answer()
    {
        var gate = new Gate();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        services.AddSingleton<ICurrentUser>(new ImpersonatedUser(Guid.CreateVersion7(), Guid.CreateVersion7()));
        services.AddSingleton(gate);
        services.AddScoped<Probe>();
        await using var root = services.BuildServiceProvider();
        using var aborted = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // the first request starts the load and is held in it; the second joins it
        var first = Task.Run(() => SendAsync(aborted.Token), Ct);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = SendAsync(Ct);

        // the first request is aborted and its services disposed
        await aborted.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(first);
        gate.Released.SetResult();

        (await second.WaitAsync(TimeSpan.FromSeconds(10), Ct)).Value.ShouldBe("loaded");

        async Task<Result<string>> SendAsync(CancellationToken cancellationToken)
        {
            await using var scope = root.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new Held(), cancellationToken);
        }
    }

    [Fact]
    public async Task Inside_an_open_transaction_cached_queries_read_through_and_are_not_cached()
    {
        var counter = new Counter();
        var dispatcher = Build(services =>
        {
            services.AddSingleton(counter);
            services.AddScoped<IUnitOfWork>(_ => new OpenTransaction());
        });

        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(1);
        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(2);
    }

    private sealed class OpenTransaction : IUnitOfWork
    {
        public bool InTransaction => true;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_middleware_ordered_like_caching_runs_once_in_a_cached_load()
    {
        var calls = new List<string>();
        var dispatcher = Build(services =>
        {
            services.AddSingleton(calls);
            services.AddSingleton(new Counter());
            // registered before the caching middleware: it sorts inside it
            services.Insert(0, ServiceDescriptor.Scoped<IRequestMiddleware, SameOrderAsCaching>());
        });

        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(1);
        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(1);

        calls.ShouldBe(["Count"]);
    }

    public sealed class SameOrderAsCaching(List<string> calls) : IRequestMiddleware
    {
        public int Order => MiddlewareOrder.Caching;

        public Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
            where TRequest : IRequest<TResult>
        {
            calls.Add(typeof(TRequest).Name);
            return next();
        }
    }

    [Fact]
    public async Task A_failed_load_reaches_every_request_that_waited_on_it_as_the_failure()
    {
        var gate = new Gate();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        services.AddSingleton<ICurrentUser>(new ImpersonatedUser(Guid.CreateVersion7(), Guid.CreateVersion7()));
        services.AddSingleton(gate);
        await using var root = services.BuildServiceProvider();

        var first = SendAsync();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = SendAsync();
        await Task.Delay(100, Ct); // the second request joins the load
        gate.Released.SetResult();

        foreach (var result in await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), Ct))
        {
            result.Error!.Code.ShouldBe("held.failed");
        }

        async Task<Result<string>> SendAsync()
        {
            await using var scope = root.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new HeldFailure(), Ct);
        }
    }

    public sealed record HeldFailure : IQuery<Result<string>>, ICachedQuery
    {
        public string CacheKey => "held-failure";

        public IReadOnlyList<string> CacheTags => [];
    }

    internal sealed class HeldFailureHandler(Gate gate) : IHandler<HeldFailure, Result<string>>
    {
        public async Task<Result<string>> HandleAsync(HeldFailure request, CancellationToken cancellationToken)
        {
            gate.Entered.TrySetResult();
            await gate.Released.Task.WaitAsync(cancellationToken);
            return Error.Conflict("held.failed", "The load failed.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_shared_load_runs_without_the_callers_ambient_values_as_its_actor(bool cancellable)
    {
        var dispatcher = Build(_ => { });
        await using var scope = Root.CreateAsyncScope();
        var actor = new ImpersonatedUser(Guid.CreateVersion7(), null);
        using var token = new CancellationTokenSource();
        Ambient.Value = "caller";

        // HybridCache runs a load inline when the caller cannot cancel, else on the pool: the load sees the same either way
        var seen = await dispatcher.Cache.GetOrCreateInScopeAsync(scope.ServiceProvider, actor, Guid.CreateVersion7().ToString(),
            (_, _) => Task.FromResult($"{Ambient.Value ?? "none"}|{CurrentUserScope.Current?.UserId}"), cancellationToken: cancellable ? token.Token : CancellationToken.None);

        seen.ShouldBe($"none|{actor.UserId}");
    }

    private static readonly ServiceProvider Root = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Anonymous_callers_and_signed_in_users_without_a_tenant_keep_apart_entries()
    {
        var counter = new Counter();
        var anonymous = Build(services => services.AddSingleton(counter).AddSingleton<ICurrentUser>(new ImpersonatedUser(null, null)));
        var host = Build(services => services.AddSingleton(counter).AddSingleton<ICurrentUser>(new ImpersonatedUser(Guid.CreateVersion7(), null)), cache: anonymous.Cache);

        (await anonymous.SendAsync(new Count(), Ct)).Value.ShouldBe(1);
        (await host.SendAsync(new Count(), Ct)).Value.ShouldBe(2);
        (await anonymous.SendAsync(new Count(), Ct)).Value.ShouldBe(1);
    }

    [Fact]
    public async Task Each_culture_has_its_own_entry()
    {
        var dispatcher = Build(_ => { });
        var previous = (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture);
        try
        {
            foreach (var culture in new[] { "de-DE", "en-US", "de-DE" })
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);
                (await dispatcher.SendAsync(new ReadCulture(), Ct)).Value.ShouldBe(culture);
            }
        }
        finally
        {
            (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture) = previous;
        }
    }

    public sealed record ReadCulture : IQuery<Result<string>>, ICachedQuery
    {
        public string CacheKey => "culture";
    }

    internal sealed class ReadCultureHandler : IHandler<ReadCulture, Result<string>>
    {
        public Task<Result<string>> HandleAsync(ReadCulture request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<string>.Success(System.Globalization.CultureInfo.CurrentUICulture.Name));
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        var counter = new Counter { FailNext = true };
        var dispatcher = Build(services => services.AddSingleton(counter));

        (await dispatcher.SendAsync(new Count(), Ct)).IsSuccess.ShouldBeFalse();

        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(2);
    }

    [Fact]
    public async Task Slow_requests_are_logged_as_warnings()
    {
        var logger = new RecordingLoggerProvider();
        var dispatcher = Build(services =>
        {
            services.AddLogging(b => b.AddProvider(logger));
            services.Configure<MessagingOptions>(o => o.SlowRequestThreshold = TimeSpan.FromMilliseconds(10));
        });

        await dispatcher.SendAsync(new Slow(), Ct);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("Slow") && e.Message.Contains("ms"));
    }

    [Fact]
    public async Task An_entry_older_than_its_refresh_time_answers_at_once_and_is_loaded_again_in_the_background()
    {
        var counter = new Counter();
        var dispatcher = Build(services => services.AddSingleton(counter));

        (await dispatcher.SendAsync(new FreshCount(), Ct)).Value.ShouldBe(1);
        (await dispatcher.SendAsync(new FreshCount(), Ct)).Value.ShouldBe(1);
        await Task.Delay(FreshAfter + TimeSpan.FromMilliseconds(200), Ct);

        (await dispatcher.SendAsync(new FreshCount(), Ct)).Value.ShouldBe(1);
        (await EventuallyAsync(() => dispatcher.SendAsync(new FreshCount(), Ct), r => r.Value == 2)).Value.ShouldBe(2);
        counter.Value.ShouldBe(2);
    }

    [Fact]
    public async Task A_refreshed_entry_reaches_another_instance_through_the_distributed_cache()
    {
        var counter = new Counter();
        var tenant = Guid.CreateVersion7();
        // a distributed cache like Redis: HybridCache skips a MemoryDistributedCache as its second level
        var shared = new SharedCache();
        void Configure(IServiceCollection services) => services.AddSingleton(counter).AddSingleton<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(shared);
        var a = Build(Configure, tenant);
        var b = Build(Configure, tenant);

        (await a.SendAsync(new FreshCount(), Ct)).Value.ShouldBe(1);
        // HybridCache writes the second level after it answered
        await EventuallyAsync(() => Task.FromResult(shared.Writes), writes => writes > 0);
        // the other instance reads the entry (value and load time) from the distributed cache instead of loading it
        (await b.SendAsync(new FreshCount(), Ct)).Value.ShouldBe(1);
        counter.Value.ShouldBe(1);

        await Task.Delay(FreshAfter + TimeSpan.FromMilliseconds(200), Ct);
        (await b.SendAsync(new FreshCount(), Ct)).Value.ShouldBe(1);
        (await EventuallyAsync(() => b.SendAsync(new FreshCount(), Ct), r => r.Value == 2)).Value.ShouldBe(2);
    }

    [Fact]
    public async Task The_cache_defaults_refresh_queries_that_name_no_refresh_time_and_can_turn_refreshing_off()
    {
        var counter = new Counter();
        var defaults = new Defaults { Value = new CacheDefaults(null, TimeSpan.FromMilliseconds(100), RefreshInBackground: true) };
        var dispatcher = Build(services => services.AddSingleton(counter).AddSingleton<ICacheDefaults>(defaults));

        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(1);
        await Task.Delay(150, Ct);
        (await dispatcher.SendAsync(new Count(), Ct)).Value.ShouldBe(1);
        (await EventuallyAsync(() => dispatcher.SendAsync(new Count(), Ct), r => r.Value == 2)).Value.ShouldBe(2);

        defaults.Value = defaults.Value with { RefreshInBackground = false };
        await Task.Delay(150, Ct);
        (await dispatcher.SendAsync(new FreshCount(), Ct)).Value.ShouldBe(3);
        await Task.Delay(FreshAfter + TimeSpan.FromMilliseconds(200), Ct);
        await dispatcher.SendAsync(new FreshCount(), Ct);
        await Task.Delay(150, Ct);
        counter.Value.ShouldBe(3);
    }

    [Fact]
    public async Task A_query_cached_by_groups_has_an_entry_per_group_selection()
    {
        var counter = new Counter();
        var groups = Substitute.For<Groups.IGroupScope>();
        IReadOnlyList<Guid> a = [Guid.CreateVersion7()], b = [Guid.CreateVersion7()];
        groups.ActiveAsync(Arg.Any<CancellationToken>()).Returns(a);
        var dispatcher = Build(services => services.AddSingleton(counter).AddSingleton(groups));

        (await dispatcher.SendAsync(new GroupCount(), Ct)).Value.ShouldBe(1);
        (await dispatcher.SendAsync(new GroupCount(), Ct)).Value.ShouldBe(1);
        groups.ActiveAsync(Arg.Any<CancellationToken>()).Returns(b);
        (await dispatcher.SendAsync(new GroupCount(), Ct)).Value.ShouldBe(2);
        groups.ActiveAsync(Arg.Any<CancellationToken>()).Returns(a);
        (await dispatcher.SendAsync(new GroupCount(), Ct)).Value.ShouldBe(1);
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var value = await read();
        for (var i = 0; i < 50 && !done(value); i++)
        {
            await Task.Delay(50, Ct);
            value = await read();
        }

        return value;
    }

    private sealed class SharedCache : Microsoft.Extensions.Caching.Distributed.IDistributedCache
    {
        private readonly Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache _inner =
            new(Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions()));

        public byte[]? Get(string key) => _inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

        public void Set(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options) => _inner.Set(key, value, options);

        public int Writes;

        public Task SetAsync(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Interlocked.Increment(ref Writes);
            return _inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public void Remove(string key) => _inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
    }

    private sealed class Defaults : ICacheDefaults
    {
        public CacheDefaults Value { get; set; } = new(null, null, true);

        public Task<CacheDefaults> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Value);
    }

    // long enough that two calls in a row on a slow runner both find the entry fresh
    private static readonly TimeSpan FreshAfter = TimeSpan.FromSeconds(1);

    public sealed record FreshCount : IQuery<Result<int>>, ICachedQuery
    {
        public string CacheKey => "fresh-count";

        public TimeSpan? CacheRefreshAfter => FreshAfter;
    }

    public sealed record GroupCount : IQuery<Result<int>>, ICachedQuery
    {
        public string CacheKey => "group-count";

        public bool CacheByGroups => true;
    }

    internal sealed class FreshCountHandler(Counter counter) : IHandler<FreshCount, Result<int>>
    {
        public Task<Result<int>> HandleAsync(FreshCount request, CancellationToken cancellationToken) => Task.FromResult(Result<int>.Success(++counter.Value));
    }

    internal sealed class GroupCountHandler(Counter counter) : IHandler<GroupCount, Result<int>>
    {
        public Task<Result<int>> HandleAsync(GroupCount request, CancellationToken cancellationToken) => Task.FromResult(Result<int>.Success(++counter.Value));
    }

    private static TestDispatcher Build(Action<IServiceCollection> configure, Guid? tenant = null, HybridCache? cache = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        services.AddSingleton<ICurrentUser>(new ImpersonatedUser(Guid.CreateVersion7(), tenant ?? Guid.CreateVersion7()));
        if (cache is not null)
        {
            services.AddSingleton(cache);
        }

        configure(services);
        var provider = services.BuildServiceProvider().CreateScope().ServiceProvider;
        return new TestDispatcher(provider.GetRequiredService<IDispatcher>(), provider.GetRequiredService<HybridCache>());
    }

    private sealed record TestDispatcher(IDispatcher Dispatcher, HybridCache Cache)
    {
        public Task<T> SendAsync<T>(IRequest<T> request, CancellationToken cancellationToken) => Dispatcher.SendAsync(request, cancellationToken);
    }

    [DependsOn(typeof(CoworkeeApplicationModule))]
    private sealed class TestModule : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) =>
            context.Services.AddMessagingFromAssembly(typeof(BehaviorTests).Assembly);
    }

    public sealed class Counter
    {
        public int Value { get; set; }

        public bool FailNext { get; set; }
    }

    public sealed record Greet(string Name) : IQuery<string>;

    public sealed record Count : IQuery<Result<int>>, ICachedQuery
    {
        public string CacheKey => "count";

        public IReadOnlyList<string> CacheTags => ["counter"];
    }

    public sealed record GlobalCount : IQuery<Result<int>>, ICachedQuery
    {
        public string CacheKey => "global-count";

        public IReadOnlyList<string> CacheTags => ["counter"];

        public CacheScope CacheScope => CacheScope.Global;
    }

    internal sealed class GlobalCountHandler(Counter counter) : IHandler<GlobalCount, Result<int>>
    {
        public Task<Result<int>> HandleAsync(GlobalCount request, CancellationToken cancellationToken) => Task.FromResult(Result<int>.Success(++counter.Value));
    }

    public sealed record ReadAmbient : IQuery<Result<string>>, ICachedQuery
    {
        public string CacheKey => "ambient";

        public IReadOnlyList<string> CacheTags => [];
    }

    internal sealed class ReadAmbientHandler : IHandler<ReadAmbient, Result<string>>
    {
        public Task<Result<string>> HandleAsync(ReadAmbient request, CancellationToken cancellationToken) => Task.FromResult(Result<string>.Success(Ambient.Value ?? "none"));
    }

    public sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class Probe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    public sealed record Held : IQuery<Result<string>>, ICachedQuery
    {
        public string CacheKey => "held";

        public IReadOnlyList<string> CacheTags => [];
    }

    internal sealed class HeldHandler(Gate gate, Probe probe) : IHandler<Held, Result<string>>
    {
        public async Task<Result<string>> HandleAsync(Held request, CancellationToken cancellationToken)
        {
            gate.Entered.TrySetResult();
            await gate.Released.Task.WaitAsync(cancellationToken);
            return probe.Disposed ? Error.Conflict("held.disposed", "The services of the load were disposed.") : Result<string>.Success("loaded");
        }
    }

    public sealed record Bump : ICommand<Result>, IInvalidatesCache
    {
        public IReadOnlyList<string> CacheTags => ["counter"];
    }

    public sealed record Slow : IQuery<string>;

    internal sealed class GreetHandler : IHandler<Greet, string>
    {
        public Task<string> HandleAsync(Greet request, CancellationToken cancellationToken) => Task.FromResult($"Hello {request.Name}");
    }

    internal sealed class CountHandler(Counter counter) : IHandler<Count, Result<int>>
    {
        public Task<Result<int>> HandleAsync(Count request, CancellationToken cancellationToken)
        {
            counter.Value++;
            if (counter.FailNext)
            {
                counter.FailNext = false;
                return Task.FromResult<Result<int>>(Error.Conflict("count.failed", "Failed"));
            }

            return Task.FromResult(Result<int>.Success(counter.Value));
        }
    }

    internal sealed class BumpHandler : IHandler<Bump, Result>
    {
        public Task<Result> HandleAsync(Bump request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    internal sealed class SlowHandler : IHandler<Slow, string>
    {
        public async Task<string> HandleAsync(Slow request, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            return "done";
        }
    }

    public sealed class Tracing<TRequest, TResult>(List<string> calls) : IRequestBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            calls.Add($"trace {typeof(TRequest).Name}");
            return next();
        }
    }

    public sealed class GreetOnly(List<string> calls) : IRequestBehavior<Greet, string>
    {
        public async Task<string> HandleAsync(Greet request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            calls.Add("greet only");
            return await next() + "!";
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recorder(Entries);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
