using System.Linq.Expressions;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Security;
using Coworkee.OData;
using Coworkee.OData.Computed;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows;

/// <summary>
/// <c>WorkflowState</c> and <c>WorkflowAwaitsMe</c> on every OData set whose entity has a workflow: a subquery on the workflow tables
/// (tenant filtered like every query), or the mirrored column with <c>MirrorTo</c>; the state facet lists the states of the definition.
/// </summary>
internal sealed class WorkflowODataProperties(WorkflowOptions options, WorkflowUser user) : IODataComputedPropertySource
{
    public const string State = "WorkflowState";
    public const string AwaitsMe = "WorkflowAwaitsMe";

    public IEnumerable<ODataComputedProperty> Properties(ODataEntityRegistry registry)
    {
        foreach (var entity in registry.Entities.Where(e => e.EntityType.GetProperty("Id")?.PropertyType == typeof(Guid)))
        {
            var workflows = options.Workflows.Values.Where(w => w.ClrType == entity.EntityType).ToList();
            // a type with several workflows takes the one named like its set
            if ((workflows.FirstOrDefault(w => string.Equals(w.EntityType, entity.EntitySet, StringComparison.OrdinalIgnoreCase)) ?? workflows.FirstOrDefault()) is not { } workflow)
            {
                continue;
            }

            var facet = new ODataComputedFacet("Workflow state",
            [
                .. workflow.StateNames.Select(s => new ODataComputedFacetOption(s, s, $"{State} eq '{s.Replace("'", "''")}'", workflow.FindState(s)?.Color)),
                new ODataComputedFacetOption("me", "Awaiting my approval", $"{AwaitsMe} eq true"),
            ]);
            yield return new ODataComputedProperty(entity.EntityType, State, typeof(string), row => StateOf(workflow, row))
            {
                Permission = WorkflowPermissions.View,
                // a mirrored state brings its own facet: [ProvideFacet] on the mirrored property, which client entities can count too
                Facet = workflow.MirrorProperty is null ? facet : null,
            };
            yield return new ODataComputedProperty(entity.EntityType, AwaitsMe, typeof(bool), row => AwaitsMeOf(workflow, row)) { Permission = WorkflowPermissions.View };
        }
    }

    private Expression StateOf(EntityWorkflow workflow, Expression row)
    {
        var initial = Expression.Constant(workflow.InitialState, typeof(string));
        if (workflow.MirrorProperty is { } mirror)
        {
            return Expression.Coalesce(Expression.Property(row, mirror), initial);
        }

        var type = workflow.EntityType;
        Expression<Func<IQueryable<WorkflowItem>, Guid, string?>> state = (items, id) =>
            items.Where(i => i.EntityType == type && i.EntityId == id).Select(i => i.State).FirstOrDefault();
        return Expression.Coalesce(Inline(state, Root<WorkflowItem>(), Expression.Property(row, "Id")), initial);
    }

    private Expression AwaitsMeOf(EntityWorkflow workflow, Expression row)
    {
        var type = workflow.EntityType;
        Expression<Func<IQueryable<WorkflowApproval>, Guid, Guid?, bool>> awaits = (approvals, id, me) =>
            approvals.Any(a => a.EntityType == type && a.EntityId == id && a.Status == ApprovalStatus.Pending && a.Approvers.Any(x => x.UserId == me && x.Approved == null));
        return Inline(awaits, Root<WorkflowApproval>(), Expression.Property(row, "Id"), Expression.Property(Expression.Constant(user), nameof(WorkflowUser.Id)));
    }

    private static WorkflowQueryRoot Root<TEntity>() => new(typeof(TEntity));

    private static Expression Inline(LambdaExpression template, params Expression[] arguments)
    {
        var body = template.Body;
        for (var i = 0; i < arguments.Length; i++)
        {
            body = ReplacingExpressionVisitor.Replace(template.Parameters[i], arguments[i], body);
        }

        return body;
    }
}

/// <summary>The user a query runs for, read by EF when it executes the query (not when it is bound).</summary>
internal sealed class WorkflowUser(IHttpContextAccessor http)
{
    public Guid? Id => CurrentUserScope.Current is { } ambient ? ambient.UserId : http.HttpContext?.RequestServices.GetService<ICurrentUser>()?.UserId;
}
