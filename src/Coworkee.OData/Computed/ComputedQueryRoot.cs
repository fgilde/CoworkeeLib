using System.Linq.Expressions;

namespace Coworkee.OData.Computed;

/// <summary>
/// A table inside a computed property (<see cref="ODataComputedProperty.Bind"/>), bound without a DbContext. EF refuses a query root of another context,
/// so the table stays a placeholder until <see cref="ComputedQueryRoots"/> swaps in the root of the executing context, whose query filters (tenant) apply.
/// </summary>
public sealed class ComputedQueryRoot(Type entityType) : Expression
{
    public Type EntityType { get; } = entityType;

    public override ExpressionType NodeType => ExpressionType.Extension;

    public override Type Type => typeof(IQueryable<>).MakeGenericType(EntityType);

    public static ComputedQueryRoot Of<TEntity>() => new(typeof(TEntity));

    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

    // compiled queries are cached by their expression
    public override bool Equals(object? obj) => obj is ComputedQueryRoot other && other.EntityType == EntityType;

    public override int GetHashCode() => EntityType.GetHashCode();

    public override string ToString() => $"ComputedQueryRoot<{EntityType.Name}>";
}
