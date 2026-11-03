using Coworkee.Application.Authorization;
using Coworkee.Contracts.Features;

namespace Coworkee.Features;

internal sealed class FeaturePermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(FeaturePermissions.GroupName, "Tenants and editions")
            .Add(FeaturePermissions.Tenants, "Manage tenants")
            .Add(FeaturePermissions.Editions, "Manage editions");
}
