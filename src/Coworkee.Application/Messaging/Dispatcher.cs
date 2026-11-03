using System.Collections.Concurrent;

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
