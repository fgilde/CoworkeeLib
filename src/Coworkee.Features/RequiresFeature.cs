using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Core;
using Coworkee.Core.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Features;

/// <summary>The request is only handled while the bool feature is on for the current tenant.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresFeatureAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}

public static class FeatureEndpointExtensions
{
    /// <summary>Answers 403 while the bool feature is off for the current tenant.</summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string feature)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
            await context.HttpContext.RequestServices.GetRequiredService<IFeatureChecker>().IsEnabledAsync(feature, context.HttpContext.RequestAborted)
                ? await next(context)
                : FeatureMiddleware.Disabled(feature).ToProblem());
}

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
