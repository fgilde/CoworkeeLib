using System.Linq.Expressions;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.OData.Edm;
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

/// <summary>A computed property in <c>$select</c> is projected from its expression, so the database evaluates it and the row carries its value.</summary>
internal sealed class ComputedSelectExpandBinder(IFilterBinder filterBinder, IOrderByBinder orderByBinder, ODataComputedProperties computed)
    : SelectExpandBinder(filterBinder, orderByBinder)
{
    public override Expression CreatePropertyValueExpression(
        QueryBinderContext context, IEdmStructuredType elementType, IEdmProperty edmProperty, Expression source, FilterClause filterClause,
        ComputeClause computeClause = null!, SearchClause search = null!)
    {
        if (computed.Find(edmProperty) is not { } property)
        {
            return base.CreatePropertyValueExpression(context, elementType, edmProperty, source, filterClause, computeClause, search);
        }

        var value = property.Bind(source);
        return value.Type.IsValueType && Nullable.GetUnderlyingType(value.Type) is null ? Expression.Convert(value, typeof(Nullable<>).MakeGenericType(value.Type)) : value;
    }
}
