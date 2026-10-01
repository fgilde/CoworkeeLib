namespace Coworkee.Application.Messaging;

public interface IRequest<TResult>;

public interface ICommand<TResult> : IRequest<TResult>;

public interface IQuery<TResult> : IRequest<TResult>;

public interface IHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}
