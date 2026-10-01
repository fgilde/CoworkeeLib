using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

internal sealed class UnitOfWorkMiddleware(IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.UnitOfWork;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var result = await next();
        if (request is not ICommand<TResult> || result is Result { IsSuccess: false })
        {
            return result;
        }

        if (services.GetService<IUnitOfWork>() is { } unitOfWork)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
