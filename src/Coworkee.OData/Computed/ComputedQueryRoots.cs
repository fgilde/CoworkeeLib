using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.OData.Computed;

internal sealed class ComputedQueryRoots : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
        new Replacer(eventData.Context!).Visit(queryExpression);

    private sealed class Replacer(Microsoft.EntityFrameworkCore.DbContext context) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node) =>
            node is ComputedQueryRoot root ? new EntityQueryRootExpression(context.Model.FindEntityType(root.EntityType)!) : base.VisitExtension(node);
    }
}
