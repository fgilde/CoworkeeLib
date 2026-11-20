using Coworkee.Application.Authorization;
using Coworkee.Contracts.Notifications;

namespace Coworkee.Notifications;

internal sealed class NotificationPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(NotificationPermissions.GroupName, "Notifications").Add(NotificationPermissions.Send, "Send notifications");
}
