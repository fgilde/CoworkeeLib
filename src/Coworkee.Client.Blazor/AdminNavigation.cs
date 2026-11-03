namespace Coworkee.Client.Blazor;

internal sealed class AdminNavigation : INavigationContributor
{
    public const string AdminGroup = Navigation.NavigationGroups.Administration;

    private const string Identity = Navigation.NavigationGroups.Identity;
    private const string System = Navigation.NavigationGroups.System;
    private const string Communication = Navigation.NavigationGroups.Communication;
    private const string Monitoring = Navigation.NavigationGroups.Monitoring;
    private const string Localization = Navigation.NavigationGroups.Localization;

    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Users", "/admin/users", MudBlazor.Icons.Material.Outlined.Person, Coworkee.Contracts.Identity.IdentityPermissions.Users.View, Group: Identity),
        new("Groups", "/admin/groups", MudBlazor.Icons.Material.Outlined.Groups, Coworkee.Contracts.Identity.IdentityPermissions.Groups.View, Group: Identity, Order: 1),
        new("Roles", "/admin/roles", MudBlazor.Icons.Material.Outlined.Shield, Coworkee.Contracts.Identity.IdentityPermissions.Roles.View, Group: Identity, Order: 2),
        new("Applications", "/admin/clients", MudBlazor.Icons.Material.Outlined.Apps, Coworkee.Contracts.Identity.IdentityPermissions.Clients.Manage, Group: Identity, Order: 3, HostOnly: true),
        new("Scopes", "/admin/scopes", MudBlazor.Icons.Material.Outlined.Key, Coworkee.Contracts.Identity.IdentityPermissions.Clients.Manage, Group: Identity, Order: 4, HostOnly: true),
        new("API keys", "/admin/api-keys", MudBlazor.Icons.Material.Outlined.VpnKey, Coworkee.Contracts.Identity.ApiKeyPermissions.Manage, Group: Identity, Order: 5),
        new("Settings", "/admin/settings", MudBlazor.Icons.Material.Outlined.Tune, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: System),
        new("AI", "/admin/ai-settings", MudBlazor.Icons.Material.Outlined.AutoAwesome, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: System, Order: 6, HostOnly: true),
        new("Mail settings", "/admin/mail-settings", MudBlazor.Icons.Material.Outlined.MarkEmailRead, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: Communication, Order: 2),
        new("Services", "/admin/services", MudBlazor.Icons.Material.Outlined.Hub, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: System, Order: 1, HostOnly: true),
        new("Tenants", "/admin/tenants", MudBlazor.Icons.Material.Outlined.Domain, Coworkee.Contracts.Features.FeaturePermissions.Tenants, Group: System, Order: 2, HostOnly: true),
        new("Editions", "/admin/editions", MudBlazor.Icons.Material.Outlined.WorkspacePremium, Coworkee.Contracts.Features.FeaturePermissions.Editions, Group: System, Order: 3, HostOnly: true),
        new("Themes", "/admin/themes", MudBlazor.Icons.Material.Outlined.Palette, Coworkee.Contracts.Theming.ThemePermissions.Manage, Group: System, Order: 4),
        new("Import and export", "/admin/data-transfer", MudBlazor.Icons.Material.Outlined.ImportExport, Coworkee.Contracts.Data.DataTransferPermissions.Use, Group: System, Order: 7),
        new("Scheduled exports", "/data/export-schedules", MudBlazor.Icons.Material.Outlined.ScheduleSend, Coworkee.Contracts.Data.DataTransferPermissions.ExportSchedules, Group: System, Order: 8),
        new("Backups", "/admin/backups", MudBlazor.Icons.Material.Outlined.Backup, Coworkee.Contracts.Backup.BackupPermissions.Manage, Group: System, Order: 5, HostOnly: true),
        new("Webhooks", "/admin/webhooks", MudBlazor.Icons.Material.Outlined.Webhook, Coworkee.Contracts.Webhooks.WebhookPermissions.Manage, Group: System, Order: 7),
        new("Mail templates", "/admin/mail/templates", MudBlazor.Icons.Material.Outlined.Email, Coworkee.Contracts.Mailing.MailPermissions.Templates.Manage, Group: Communication),
        new("Mail campaigns", "/admin/mail/campaigns", MudBlazor.Icons.Material.Outlined.Campaign, Coworkee.Contracts.Mailing.MailPermissions.Campaigns.Send, Group: Communication, Order: 3),
        new("Mail log", "/admin/mail/log", MudBlazor.Icons.Material.Outlined.Outbox, Coworkee.Contracts.Mailing.MailPermissions.Log.View, Group: Communication, Order: 1),
        new("Languages", "/admin/languages", MudBlazor.Icons.Material.Outlined.Language, Coworkee.Contracts.Localization.LocalizationPermissions.Manage, Group: Localization),
        new("Translations", "/admin/translations", MudBlazor.Icons.Material.Outlined.Translate, Coworkee.Contracts.Localization.LocalizationPermissions.Manage, Group: Localization, Order: 1),
        new("Audit log", "/admin/audit", MudBlazor.Icons.Material.Outlined.History, Coworkee.Contracts.Auditing.AuditPermissions.View, Group: Monitoring),
        new("System status", "/admin/system-status", MudBlazor.Icons.Material.Outlined.MonitorHeart, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: Monitoring, Order: -1, HostOnly: true),
        new("AI tool calls", "/admin/ai-tool-calls", MudBlazor.Icons.Material.Outlined.ManageSearch, Coworkee.Contracts.Ai.AiPermissions.Audit, Group: Monitoring, Order: 1),
        new("Retention", "/admin/retention", MudBlazor.Icons.Material.Outlined.AutoDelete, Coworkee.Contracts.Retention.RetentionPermissions.Manage, Group: Monitoring, Order: 2, HostOnly: true),
        new("Files", "/files", MudBlazor.Icons.Material.Outlined.FolderOpen, Coworkee.Contracts.Files.FilePermissions.View, Order: -8),
        new("Chat", "/chat", MudBlazor.Icons.Material.Outlined.Chat, Coworkee.Contracts.Chat.ChatPermissions.Use, Order: -9),
        new("Approvals", "/workflows/approvals", MudBlazor.Icons.Material.Outlined.FactCheck, Coworkee.Contracts.Workflows.WorkflowPermissions.View, Order: -7),
        new("Workflow designer", "/api/v1/workflows/elsa/studio", MudBlazor.Icons.Material.Outlined.AccountTree, Coworkee.Contracts.Workflows.WorkflowPermissions.Elsa, ForceLoad: true, Group: System, Order: 7),
    ];
}
