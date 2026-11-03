namespace Coworkee.Application.Messaging;

internal abstract class RequestInvoker<TResult>
{
    public abstract Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken);
}
