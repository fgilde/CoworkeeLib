using Coworkee.Core;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Http;

internal sealed class CoworkeeExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var error = exception switch
        {
            ValidationException validation => Error.Validation(validation.Errors
                .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal)),
            ConcurrencyConflictException conflict => Error.Conflict("concurrency.conflict", conflict.Message),
            ForbiddenException forbidden => Error.Forbidden("auth.forbidden", forbidden.Message),
            UnauthorizedAccessException unauthorized => Error.Unauthorized("auth.required", unauthorized.Message),
            _ => null,
        };

        if (error is null)
        {
            return false;
        }

        await error.ToProblem().ExecuteAsync(httpContext);
        return true;
    }
}
