using Coworkee.Application.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nextended.ResponseFilters;
using Nextended.ResponseFilters.Builders;

namespace Coworkee.ResponseFilters;

public static class PermissionRuleExtensions
{
    /// <summary>Applies the rule only to callers without the permission, e.g. <c>Remove(x => x.Cost).UnlessGranted("Catalog.Costs.View")</c>.</summary>
    public static ResponseFilter<T> UnlessGranted<TBuilder, T>(this RuleBuilderBase<TBuilder, T> rule, string permission)
        where TBuilder : RuleBuilderBase<TBuilder, T>
        where T : class =>
        rule.Unless((IResponseFilterContext context) => IsGrantedAsync(context, permission));

    public static ResponseFilter<T> WhenGranted<TBuilder, T>(this RuleBuilderBase<TBuilder, T> rule, string permission)
        where TBuilder : RuleBuilderBase<TBuilder, T>
        where T : class =>
        rule.When((IResponseFilterContext context) => IsGrantedAsync(context, permission));

    private static Task<bool> IsGrantedAsync(IResponseFilterContext context, string permission) =>
        context.Services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(permission, context.CancellationToken);
}
