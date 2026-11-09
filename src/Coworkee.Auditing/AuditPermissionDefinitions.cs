using Coworkee.Application.Authorization;
using Coworkee.Contracts.Auditing;

namespace Coworkee.Auditing;

internal sealed class AuditPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(AuditPermissions.GroupName, "Audit")
            .Add(AuditPermissions.View, "View audit log")
            .Add(AuditPermissions.SecurityLog, "View security log")
            .Add(AuditPermissions.Requests, "View request log")
            .Add(AuditPermissions.RequestBodies, "View request and response bodies", AuditPermissions.Requests);
}
