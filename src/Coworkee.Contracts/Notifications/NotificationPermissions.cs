namespace Coworkee.Contracts.Notifications;

public static class NotificationPermissions
{
    public const string GroupName = "Notifications";

    /// <summary>Sends notifications to users chosen by hand or by filter (the Send notification page); choosing them also needs <c>Identity.Users.View</c>.</summary>
    public const string Send = "Notifications.Send";
}
