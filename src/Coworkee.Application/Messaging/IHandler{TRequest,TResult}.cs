namespace Coworkee.Application.Messaging;

public interface IHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
