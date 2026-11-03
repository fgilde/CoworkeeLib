using Coworkee.Application.Authorization;
using Coworkee.Contracts.Workflows;

namespace Coworkee.Workflows;

internal sealed class WorkflowPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(WorkflowPermissions.GroupName, "Workflows and approvals")
            .Add(WorkflowPermissions.View, "View workflows and own approvals")
            .Add(WorkflowPermissions.Transition, "Run workflow transitions", WorkflowPermissions.View);
}
