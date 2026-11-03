namespace Coworkee.Contracts.Webhooks;

public static class WebhookPermissions
{
    public const string GroupName = "Webhooks";

    /// <summary>Register, change and test the organisation's webhook endpoints and read their delivery log.</summary>
    public const string Manage = "Webhooks.Manage";
}
