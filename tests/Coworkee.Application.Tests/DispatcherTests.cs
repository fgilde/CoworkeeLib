using Coworkee.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Tests;

public sealed class DispatcherTests
{
    [Fact]
    public async Task Sends_request_to_its_handler()
    {
        await using var provider = Build();

        var result = await Dispatcher(provider).SendAsync(new Echo("hi"), TestContext.Current.CancellationToken);

        result.ShouldBe("hi");
    }

    [Fact]
    public async Task Runs_middlewares_by_order_outermost_first()
    {
        var log = new List<string>();
        await using var provider = Build(services =>
        {
            services.AddSingleton(log);
            services.AddScoped<IRequestMiddleware>(_ => new Recording(log, "inner", 20));
            services.AddScoped<IRequestMiddleware>(_ => new Recording(log, "outer", 10));
        });

        await Dispatcher(provider).SendAsync(new Echo("x"), TestContext.Current.CancellationToken);

        log.ShouldBe(["outer:before", "inner:before", "inner:after", "outer:after"]);
    }

    [Fact]
    public async Task Missing_handler_fails_with_clear_message()
    {
        await using var provider = Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => Dispatcher(provider).SendAsync(new Unhandled(), TestContext.Current.CancellationToken));

        ex.Message.ShouldContain(nameof(Unhandled));
    }

    private static IDispatcher Dispatcher(ServiceProvider provider) =>
        provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();

    private static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddDispatcher();
        services.AddMessagingFromAssembly(typeof(DispatcherTests).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public sealed record Echo(string Text) : IQuery<string>;

    public sealed record Unhandled : IQuery<int>;

    internal sealed class EchoHandler : IHandler<Echo, string>
    {
        public Task<string> HandleAsync(Echo request, CancellationToken cancellationToken) => Task.FromResult(request.Text);
    }

    private sealed class Recording(List<string> log, string name, int order) : IRequestMiddleware
    {
        public int Order => order;

        public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
            where TRequest : IRequest<TResult>
        {
            log.Add($"{name}:before");
            var result = await next();
            log.Add($"{name}:after");
            return result;
        }
    }
}
