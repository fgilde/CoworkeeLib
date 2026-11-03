using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Core;
using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Features;

internal sealed class FeatureMiddleware(IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Features;

    public static Error Disabled(string feature) => Error.Forbidden("feature.disabled", $"Feature '{feature}' is not enabled.");

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        if (Requirements<TRequest>.Features.Length > 0)
        {
            var checker = services.GetRequiredService<IFeatureChecker>();
            foreach (var feature in Requirements<TRequest>.Features)
            {
                if (!await checker.IsEnabledAsync(feature, cancellationToken))
                {
                    return ResultFactory.TryCreateFailure<TResult>(Disabled(feature), out var failed)
                        ? failed
                        : throw new ForbiddenException($"Feature '{feature}' is not enabled.");
                }
            }
        }

        return await next();
    }

    private static class Requirements<TRequest>
    {
        public static readonly string[] Features =
            typeof(TRequest).GetCustomAttributes<RequiresFeatureAttribute>().Select(a => a.Feature).ToArray();
    }
}
