using System.Linq.Expressions;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.OData.UriParser;

namespace Coworkee.OData.Computed;

internal sealed class ComputedFilterBinder(ODataComputedProperties computed) : FilterBinder
{
    public override Expression BindPropertyAccessQueryNode(SingleValuePropertyAccessNode propertyAccessNode, QueryBinderContext context) =>
        computed.Find(propertyAccessNode.Property) is { } property
            ? property.Bind(Bind(propertyAccessNode.Source, context))
            : base.BindPropertyAccessQueryNode(propertyAccessNode, context);
}

internal sealed class ComputedOrderByBinder(ODataComputedProperties computed) : OrderByBinder
{
    public override Expression BindPropertyAccessQueryNode(SingleValuePropertyAccessNode propertyAccessNode, QueryBinderContext context) =>
        computed.Find(propertyAccessNode.Property) is { } property
            ? property.Bind(Bind(propertyAccessNode.Source, context))
            : base.BindPropertyAccessQueryNode(propertyAccessNode, context);
}
