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
    public override Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var typed = (TRequest)request;
        var handler = services.GetService<IHandler<TRequest, TResult>>()
            ?? throw new InvalidOperationException($"No handler registered for '{typeof(TRequest).Name}'.");

        RequestHandlerDelegate<TResult> pipeline = () => handler.HandleAsync(typed, cancellationToken);
        foreach (var middleware in services.GetServices<IRequestMiddleware>().OrderByDescending(m => m.Order))
        {
            var next = pipeline;
            pipeline = () => middleware.InvokeAsync(typed, next, cancellationToken);
        }

        return pipeline();
    }
}
