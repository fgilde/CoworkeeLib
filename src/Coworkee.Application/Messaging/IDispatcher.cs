namespace Coworkee.Application.Messaging;

public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}
