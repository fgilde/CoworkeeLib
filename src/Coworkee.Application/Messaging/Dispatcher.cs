using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoker = (RequestInvoker<TResult>)Invokers.GetOrAdd(
            request.GetType(),
            type => Activator.CreateInstance(typeof(RequestInvoker<,>).MakeGenericType(type, typeof(TResult)))!);
        return invoker.InvokeAsync(request, services, cancellationToken);
    }
}

internal abstract class RequestInvoker<TResult>
{
    public abstract Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class RequestInvoker<TRequest, TResult> : RequestInvoker<TResult>
    where TRequest : IRequest<TResult>
{
    public override Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken) =>
        Pipeline((TRequest)request, services, cancellationToken)();

    /// <summary>
    /// The handler with its behaviors and middlewares from <paramref name="services"/>; with <paramref name="inside"/> only the middlewares that run
    /// inside that one, by their place in the pipeline (an equal order sorts by registration).
    /// </summary>
    internal static RequestHandlerDelegate<TResult> Pipeline(TRequest typed, IServiceProvider services, CancellationToken cancellationToken, Type? inside = null)
    {
        var handler = services.GetService<IHandler<TRequest, TResult>>()
            ?? throw new InvalidOperationException($"No handler registered for '{typeof(TRequest).Name}'.");

        RequestHandlerDelegate<TResult> pipeline = () => handler.HandleAsync(typed, cancellationToken);
        foreach (var behavior in services.GetServices<IRequestBehavior<TRequest, TResult>>().Reverse())
        {
            var inner = pipeline;
            pipeline = () => behavior.HandleAsync(typed, inner, cancellationToken);
        }

        var middlewares = services.GetServices<IRequestMiddleware>().OrderByDescending(m => m.Order);
        foreach (var middleware in inside is null ? middlewares : middlewares.TakeWhile(m => m.GetType() != inside))
        {
            var next = pipeline;
            pipeline = () => middleware.InvokeAsync(typed, next, cancellationToken);
        }

        return pipeline;
    }
}
