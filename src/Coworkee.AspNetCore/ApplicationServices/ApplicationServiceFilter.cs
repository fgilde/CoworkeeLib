using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Core;
using Coworkee.Core.Security;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AspNetCore.ApplicationServices;

/// <summary>Around every application service call: [RequiresPermission], FluentValidation of the arguments and the unit of work.</summary>
internal sealed class ApplicationServiceFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        await AuthorizeAsync(http);
        await ValidateAsync(context.Arguments, http.RequestServices, http.RequestAborted);
        var result = await next(context);
        if (!HttpMethods.IsGet(http.Request.Method) && http.RequestServices.GetService<IUnitOfWork>() is { } unitOfWork)
        {
            await unitOfWork.SaveChangesAsync(http.RequestAborted);
        }

        return result;
    }

    private static async Task AuthorizeAsync(HttpContext http)
    {
        var required = http.GetEndpoint()?.Metadata.GetOrderedMetadata<RequiresPermissionAttribute>() ?? [];
        if (required.Count == 0)
        {
            return;
        }

        if (!http.RequestServices.GetRequiredService<ICurrentUser>().IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        var checker = http.RequestServices.GetRequiredService<IPermissionChecker>();
        foreach (var permission in required.Select(r => r.Permission).Distinct(StringComparer.Ordinal))
        {
            if (!await checker.IsGrantedAsync(permission, http.RequestAborted))
            {
                throw new ForbiddenException($"Permission '{permission}' is required.");
            }
        }
    }

    private static async Task ValidateAsync(IList<object?> arguments, IServiceProvider services, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        foreach (var argument in arguments.OfType<object>())
        {
            foreach (var validator in services.GetServices(typeof(IValidator<>).MakeGenericType(argument.GetType())).OfType<IValidator>())
            {
                failures.AddRange((await validator.ValidateAsync(new ValidationContext<object>(argument), cancellationToken)).Errors);
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
