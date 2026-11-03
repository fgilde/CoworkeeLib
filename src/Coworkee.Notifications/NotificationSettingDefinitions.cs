using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Notifications;

internal sealed class NotificationSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Notifications", "Notifications")
            .Add(NotificationDigestJob.Setting, "Daily e-mail with unread notifications", SettingType.Bool, [SettingScope.Global, SettingScope.User], "true", visibleToClient: true);
}
