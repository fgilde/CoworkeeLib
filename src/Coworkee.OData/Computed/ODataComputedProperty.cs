using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.OData.Computed;

/// <summary>
/// A property of an entity set that has no CLR property, e.g. the workflow state of a row: <see cref="Bind"/> turns the row into an expression
/// the database evaluates, so <c>$filter</c> and <c>$orderby</c> run on the server. It is never serialized, and stays out of reports and, unless <see cref="Exported"/>, out of exports.
/// </summary>
/// <param name="Bind">The row expression to the value, e.g. a subquery on another table; query filters of the DbContext (tenant) apply to it.</param>
public sealed record ODataComputedProperty(Type EntityType, string Name, Type Type, Func<Expression, Expression> Bind)
{
    /// <summary>Users without it cannot filter, sort or facet on the property; for them it does not exist.</summary>
    public string? Permission { get; init; }

    /// <summary>A facet group on the property; every option counts the rows its own OData filter matches.</summary>
    public ODataComputedFacet? Facet { get; init; }

    /// <summary>A column of exports after the entity's own, e.g. a value an import reads back.</summary>
    public bool Exported { get; init; }

    /// <summary>The body of <paramref name="template"/> with its parameters replaced by <paramref name="arguments"/>, e.g. a <see cref="ComputedQueryRoot"/> and the row's key.</summary>
    public static Expression Inline(LambdaExpression template, params Expression[] arguments)
    {
        var body = template.Body;
        for (var i = 0; i < arguments.Length; i++)
        {
            body = ReplacingExpressionVisitor.Replace(template.Parameters[i], arguments[i], body);
        }

        return body;
    }
}

public sealed record ODataComputedFacet(string Label, IReadOnlyList<ODataComputedFacetOption> Options);

/// <param name="Color">A CSS color for swatches and chips.</param>
public sealed record ODataComputedFacetOption(string Value, string Label, string OData, string? Color = null);

/// <summary>Adds computed properties to registered entity sets; registered as a singleton, asked once when the EDM model is built.</summary>
public interface IODataComputedPropertySource
{
    IEnumerable<ODataComputedProperty> Properties(ODataEntityRegistry registry);
}
