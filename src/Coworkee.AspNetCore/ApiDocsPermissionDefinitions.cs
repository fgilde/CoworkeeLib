using Coworkee.Application.Authorization;
using Coworkee.Contracts.ApiDocs;

namespace Coworkee.AspNetCore;

internal sealed class ApiDocsPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(ApiDocsPermissions.GroupName, "API documentation").Add(ApiDocsPermissions.View, "View API documentation");
}
