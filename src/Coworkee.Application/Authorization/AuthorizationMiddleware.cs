using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Core;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Authorization;

internal sealed class AuthorizationMiddleware(ICurrentUser currentUser, IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Authorization;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var required = Requirements<TRequest>.Permissions;
        var resource = request as IResourceRequest;
        if (required.Length == 0 && resource is null)
        {
            return await next();
        }

        if (!currentUser.IsAuthenticated)
        {
            return Fail<TResult>(Error.Unauthorized("auth.required", "Authentication is required."), () => new UnauthorizedAccessException("Authentication is required."));
        }

        var checker = services.GetRequiredService<IPermissionChecker>();
        foreach (var permission in required)
        {
            if (!await checker.IsGrantedAsync(permission, cancellationToken))
            {
                return Denied<TResult>(permission);
            }
        }

        if (resource is not null
            && !await checker.IsGrantedAsync(resource.RequiredPermission, resource.ResourceType, resource.ResourceId, cancellationToken))
        {
            return Denied<TResult>(resource.RequiredPermission);
        }

        return await next();
    }

    private static TResult Denied<TResult>(string permission) =>
        Fail<TResult>(Error.Forbidden("auth.forbidden", $"Permission '{permission}' is required."), () => new ForbiddenException($"Permission '{permission}' is required."));

    private static TResult Fail<TResult>(Error error, Func<Exception> exception) =>
        ResultFactory.TryCreateFailure<TResult>(error, out var failed) ? failed : throw exception();

    private static class Requirements<TRequest>
    {
        public static readonly string[] Permissions =
            typeof(TRequest).GetCustomAttributes<RequiresPermissionAttribute>().Select(a => a.Permission).ToArray();
    }
}
