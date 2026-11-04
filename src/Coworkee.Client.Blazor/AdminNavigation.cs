namespace Coworkee.Client.Blazor;

internal sealed class AdminNavigation : INavigationContributor
{
    public const string AdminGroup = Navigation.NavigationGroups.Administration;

    private const string Identity = Navigation.NavigationGroups.Identity;
    private const string System = Navigation.NavigationGroups.System;
    private const string Communication = Navigation.NavigationGroups.Communication;
    private const string Monitoring = Navigation.NavigationGroups.Monitoring;
    private const string Localization = Navigation.NavigationGroups.Localization;
    private const string UserInterface = Navigation.NavigationGroups.UserInterface;

    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Users", "/admin/users", MudBlazor.Icons.Material.Outlined.Person, Coworkee.Contracts.Identity.IdentityPermissions.Users.View, Group: Identity, Description: "Invite, edit, lock and delete users; assign roles and groups."),
        new("Groups", "/admin/groups", MudBlazor.Icons.Material.Outlined.Groups, Coworkee.Contracts.Identity.IdentityPermissions.Groups.View, Group: Identity, Order: 1, Description: "Groups of users that share roles and access."),
        new("Roles", "/admin/roles", MudBlazor.Icons.Material.Outlined.Shield, Coworkee.Contracts.Identity.IdentityPermissions.Roles.View, Group: Identity, Order: 2, Description: "Roles and the permissions they grant."),
        new("Applications", "/admin/clients", MudBlazor.Icons.Material.Outlined.Apps, Coworkee.Contracts.Identity.IdentityPermissions.Clients.Manage, Group: Identity, Order: 3, HostOnly: true, Description: "OAuth/OpenID applications that may sign in users or call the API."),
        new("Scopes", "/admin/scopes", MudBlazor.Icons.Material.Outlined.Key, Coworkee.Contracts.Identity.IdentityPermissions.Clients.Manage, Group: Identity, Order: 4, HostOnly: true, Description: "API scopes the applications may request."),
        new("API keys", "/admin/api-keys", MudBlazor.Icons.Material.Outlined.VpnKey, Coworkee.Contracts.Identity.ApiKeyPermissions.Manage, Group: Identity, Order: 5, Description: "API keys of all users: see, revoke."),
        new("Settings", "/admin/settings", MudBlazor.Icons.Material.Outlined.Tune, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: System, Description: "All settings: app name, security, defaults, per organisation and user."),
        new("AI", "/admin/ai-settings", MudBlazor.Icons.Material.Outlined.AutoAwesome, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: System, Order: 6, HostOnly: true, Description: "AI provider connection, model and limits of the assistant."),
        new("Mail settings", "/admin/mail-settings", MudBlazor.Icons.Material.Outlined.MarkEmailRead, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: Communication, Order: 2, Description: "Mail server (SMTP) and sender address."),
        new("Services", "/admin/services", MudBlazor.Icons.Material.Outlined.Hub, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: System, Order: 1, HostOnly: true, Description: "Connected services and their health."),
        new("Tenants", "/admin/tenants", MudBlazor.Icons.Material.Outlined.Domain, Coworkee.Contracts.Features.FeaturePermissions.Tenants, Group: System, Order: 2, HostOnly: true, Description: "Organisations (tenants) of the installation."),
        new("Editions", "/admin/editions", MudBlazor.Icons.Material.Outlined.WorkspacePremium, Coworkee.Contracts.Features.FeaturePermissions.Editions, Group: System, Order: 3, HostOnly: true, Description: "Editions: which features each organisation gets."),
        new("Themes", "/admin/themes", MudBlazor.Icons.Material.Outlined.Palette, Coworkee.Contracts.Theming.ThemePermissions.Manage, Group: UserInterface, Description: "Colors, fonts, logo and dark mode palette of the app; create and choose themes."),
        new("Header", "/admin/header", MudBlazor.Icons.Material.Outlined.WebAsset, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: UserInterface, Order: 1, HostOnly: true, Description: "Order the items of the app bar, move them into the overflow menu or hide them; where the assistant shows."),
        new("Assistant", "/assistant", MudBlazor.Icons.Material.Outlined.AutoAwesome, Coworkee.Contracts.Ai.AiPermissions.Chat, Order: -6, Visible: Ai.AssistantNavigation.VisibleAsync, Description: "Opens the AI assistant."),
        new("Import and export", "/admin/data-transfer", MudBlazor.Icons.Material.Outlined.ImportExport, Coworkee.Contracts.Data.DataTransferPermissions.Use, Group: System, Order: 7, Description: "Import data from files and export it (CSV, Excel, JSON)."),
        new("Scheduled exports", "/data/export-schedules", MudBlazor.Icons.Material.Outlined.ScheduleSend, Coworkee.Contracts.Data.DataTransferPermissions.ExportSchedules, Group: System, Order: 8, Description: "Exports that run on a schedule and send or store the file."),
        new("Backups", "/admin/backups", MudBlazor.Icons.Material.Outlined.Backup, Coworkee.Contracts.Backup.BackupPermissions.Manage, Group: System, Order: 5, HostOnly: true, Description: "Create, download and restore backups."),
        new("Webhooks", "/admin/webhooks", MudBlazor.Icons.Material.Outlined.Webhook, Coworkee.Contracts.Webhooks.WebhookPermissions.Manage, Group: System, Order: 7, Description: "Webhooks that notify other systems of changes."),
        new("Mail templates", "/admin/mail/templates", MudBlazor.Icons.Material.Outlined.Email, Coworkee.Contracts.Mailing.MailPermissions.Templates.Manage, Group: Communication, Description: "Texts and layout of the mails the app sends."),
        new("Mail campaigns", "/admin/mail/campaigns", MudBlazor.Icons.Material.Outlined.Campaign, Coworkee.Contracts.Mailing.MailPermissions.Campaigns.Send, Group: Communication, Order: 3, Description: "Send mails to many users at once."),
        new("Mail log", "/admin/mail/log", MudBlazor.Icons.Material.Outlined.Outbox, Coworkee.Contracts.Mailing.MailPermissions.Log.View, Group: Communication, Order: 1, Description: "Sent and failed mails."),
        new("Languages", "/admin/languages", MudBlazor.Icons.Material.Outlined.Language, Coworkee.Contracts.Localization.LocalizationPermissions.Manage, Group: Localization, Description: "Turn languages of the app on or off; choose the default."),
        new("Translations", "/admin/translations", MudBlazor.Icons.Material.Outlined.Translate, Coworkee.Contracts.Localization.LocalizationPermissions.Manage, Group: Localization, Order: 1, Description: "Edit the texts of the app per language."),
        new("Audit log", "/admin/audit", MudBlazor.Icons.Material.Outlined.History, Coworkee.Contracts.Auditing.AuditPermissions.View, Group: Monitoring, Description: "Who changed what and when."),
        new("System status", "/admin/system-status", MudBlazor.Icons.Material.Outlined.MonitorHeart, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: Monitoring, Order: -1, HostOnly: true, Description: "Health, versions, jobs and resources of the installation."),
        new("AI tool calls", "/admin/ai-tool-calls", MudBlazor.Icons.Material.Outlined.ManageSearch, Coworkee.Contracts.Ai.AiPermissions.Audit, Group: Monitoring, Order: 1, Description: "What the assistant and MCP clients did, per user."),
        new("Retention", "/admin/retention", MudBlazor.Icons.Material.Outlined.AutoDelete, Coworkee.Contracts.Retention.RetentionPermissions.Manage, Group: Monitoring, Order: 2, HostOnly: true, Description: "How long logs and old data are kept."),
        new("Files", "/files", MudBlazor.Icons.Material.Outlined.FolderOpen, Coworkee.Contracts.Files.FilePermissions.View, Order: -8, Description: "Upload, share and organise files and folders."),
        new("Chat", "/chat", MudBlazor.Icons.Material.Outlined.Chat, Coworkee.Contracts.Chat.ChatPermissions.Use, Order: -9, Description: "Chat with colleagues in channels and direct messages."),
        new("Approvals", "/workflows/approvals", MudBlazor.Icons.Material.Outlined.FactCheck, Coworkee.Contracts.Workflows.WorkflowPermissions.View, Order: -7, Description: "Approval requests waiting for you."),
        new("Workflow designer", "/api/v1/workflows/elsa/studio", MudBlazor.Icons.Material.Outlined.AccountTree, Coworkee.Contracts.Workflows.WorkflowPermissions.Elsa, ForceLoad: true, Group: System, Order: 7, Description: "Design automated workflows."),
    ];
}
