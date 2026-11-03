using Coworkee.Application.Authorization;

namespace Coworkee.Workflows.Elsa;

internal sealed class ElsaPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(ElsaPermissions.GroupName, "Elsa workflows")
            .Add(ElsaPermissions.Manage, "Design and run Elsa workflows");
}
