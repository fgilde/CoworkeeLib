using Coworkee.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Features;

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
