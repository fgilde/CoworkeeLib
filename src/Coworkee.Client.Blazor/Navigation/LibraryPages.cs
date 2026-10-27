using Coworkee.Client.Blazor.Pages;
using Coworkee.Client.Blazor.Pages.Admin;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>The pages of this assembly with their keys and old addresses; the docs list the keys apps replace or turn off.</summary>
internal static class LibraryPages
{
    public static IEnumerable<CoworkeePage> All =>
    [
        new("users", typeof(Users), "admin/users"),
        new("user", typeof(UserDetail), "admin/users/{Id:guid}"),
        new("groups", typeof(Groups), "admin/groups"),
        new("roles", typeof(Roles), "admin/roles"),
        new("role", typeof(RoleDetail), "admin/roles/{Id:guid}"),
        new("clients", typeof(Clients), "admin/clients"),
        new("scopes", typeof(Scopes), "admin/scopes"),
        new("api-keys", typeof(ApiKeys), "admin/api-keys"),
        new("settings", typeof(Settings), "admin/settings", "admin/configuration"),
        new("ai-settings", typeof(AiConfiguration), "admin/ai-settings"),
        new("ai-tool-calls", typeof(AiToolCalls), "admin/ai-tool-calls"),
        new("audit", typeof(AuditLog), "admin/audit"),
        new("backups", typeof(Backups), "admin/backups"),
        new("data-transfer", typeof(DataTransfer), "admin/data-transfer"),
        new("export-schedules", typeof(ExportSchedules), "data/export-schedules"),
        new("editions", typeof(Editions), "admin/editions"),
        new("languages", typeof(Languages), "admin/languages"),
        new("translations", typeof(Translations), "admin/translations"),
        new("mail-settings", typeof(MailConfiguration), "admin/mail-settings"),
        new("mail-templates", typeof(MailTemplates), "admin/mail/templates"),
        new("mail-template", typeof(MailTemplateDetail), "admin/mail/templates/{Name}/{Culture}"),
        new("mail-campaigns", typeof(MailCampaigns), "admin/mail/campaigns"),
        new("mail-log", typeof(MailLog), "admin/mail/log"),
        new("retention", typeof(Pages.Admin.Retention), "admin/retention"),
        new("services", typeof(Services), "admin/services"),
        new("system-status", typeof(SystemStatus), "admin/system-status"),
        new("tenants", typeof(Tenants), "admin/tenants"),
        new("themes", typeof(Themes), "admin/themes"),
        new("webhooks", typeof(Pages.Admin.Webhooks), "admin/webhooks"),
        new("approvals", typeof(Approvals), "workflows/approvals"),
        new("assistant", typeof(Assistant), "assistant"),
        new("chat", typeof(Pages.Chat), "chat"),
        new("person", typeof(Person), "people/{Id:guid}"),
        new("files", typeof(Pages.Files), "files"),
        new("notifications", typeof(Notifications), "notifications"),
        new("profile", typeof(Profile), "profile", "profile/{Tab}"),
        new("search", typeof(SearchResults), "search"),
        new("user-settings", typeof(UserSettings), "settings"),
    ];
}
