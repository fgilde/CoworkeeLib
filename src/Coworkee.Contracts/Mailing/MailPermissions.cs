namespace Coworkee.Contracts.Mailing;

public static class MailPermissions
{
    public const string GroupName = "Mail";

    public static class Templates
    {
        public const string Manage = "Mail.Templates.Manage";
    }

    public static class Log
    {
        public const string View = "Mail.Log.View";
    }

    public static class Campaigns
    {
        public const string Send = "Mail.Campaigns.Send";
    }
}
