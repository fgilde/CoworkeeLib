using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.Workflows;

/// <summary>
/// A workflow table inside a query bound without a DbContext (OData binders). EF refuses a query root of another context,
/// so the table stays a placeholder until <see cref="WorkflowQueryRoots"/> swaps in the root of the executing context, whose query filters (tenant) apply.
/// </summary>
internal sealed class WorkflowQueryRoot(Type entityType) : Expression
{
    public Type EntityType { get; } = entityType;

    public override ExpressionType NodeType => ExpressionType.Extension;

    public override Type Type => typeof(IQueryable<>).MakeGenericType(EntityType);

    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

    // compiled queries are cached by their expression
    public override bool Equals(object? obj) => obj is WorkflowQueryRoot other && other.EntityType == EntityType;

    public override int GetHashCode() => EntityType.GetHashCode();

    public override string ToString() => $"WorkflowQueryRoot<{EntityType.Name}>";
}

internal sealed class WorkflowQueryRoots : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
        new Replacer(eventData.Context!).Visit(queryExpression);

    private sealed class Replacer(Microsoft.EntityFrameworkCore.DbContext context) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node) =>
            node is WorkflowQueryRoot root ? new EntityQueryRootExpression(context.Model.FindEntityType(root.EntityType)!) : base.VisitExtension(node);
    }
}
