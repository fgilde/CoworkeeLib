using Coworkee.Application.Authorization;
using Coworkee.Contracts.Theming;

namespace Coworkee.Theming;

internal sealed class ThemePermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(ThemePermissions.GroupName, "Themes").Add(ThemePermissions.Manage, "Manage themes");
}
