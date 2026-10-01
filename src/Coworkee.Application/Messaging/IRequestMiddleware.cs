namespace Coworkee.Application.Messaging;

public delegate Task<TResult> RequestHandlerDelegate<TResult>();

public interface IRequestMiddleware
{
    int Order { get; }

    Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>;
}
