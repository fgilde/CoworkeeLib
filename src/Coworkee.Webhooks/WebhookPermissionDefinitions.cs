using Coworkee.Application.Authorization;
using Coworkee.Contracts.Webhooks;

namespace Coworkee.Webhooks;

internal sealed class WebhookPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(WebhookPermissions.GroupName, "Webhooks").Add(WebhookPermissions.Manage, "Manage webhook endpoints and read their deliveries");
}
