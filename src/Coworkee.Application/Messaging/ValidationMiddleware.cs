using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

internal sealed class ValidationMiddleware(IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Validation;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var validators = services.GetServices<IValidator<TRequest>>().ToArray();
        if (validators.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).ToArray();
        if (failures.Length == 0)
        {
            return await next();
        }

        var error = Error.Validation(failures
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal));

        return ResultFactory.TryCreateFailure<TResult>(error, out var failed) ? failed : throw new ValidationException(failures);
    }
}
