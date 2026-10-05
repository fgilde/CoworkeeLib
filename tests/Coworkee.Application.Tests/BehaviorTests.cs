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
