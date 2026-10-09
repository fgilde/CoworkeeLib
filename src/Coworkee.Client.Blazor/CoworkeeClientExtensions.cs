using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor;

public static class CoworkeeClientExtensions
{
    public static IServiceCollection AddCoworkeeClient(this IServiceCollection services, Uri baseAddress, Action<CoworkeeClientOptions>? configure = null)
    {
        var options = new CoworkeeClientOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddMudServicesWithExtensions();
        MudBlazor.Extensions.Components.ObjectEdit.Options.ObjectEditPropertyMetaSettings.DefaultLabelResolverFn = Localization.PropertyLabels.For;
        Components.Editors.CoworkeeEditors.Register();
        services.AddTransient<Localization.CultureHeaderHandler>();
        services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler<Localization.CultureHeaderHandler>());
        services.AddHttpClient<ICoworkeeApi, CoworkeeApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Data.IODataClient, Data.ODataClient>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Localization.ILocalizationApi, Localization.LocalizationApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<ExtendedAttributes.IExtendedAttributesApi, ExtendedAttributes.ExtendedAttributesApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Chat.IChatApi, Chat.ChatApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Social.ISocialApi, Social.SocialApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Features.IFeaturesApi, Features.FeaturesApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Security.IClientsApi, Security.ClientsApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Backup.IBackupApi, Backup.BackupApi>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddHttpClient<Files.IFilesApi, Files.FilesApi>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromHours(1);
        });
        services.AddHttpClient<Ai.IAssistantApi, Ai.AssistantApi>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<Localization.CoworkeeLocalizer>();
        services.AddSingleton(typeof(Microsoft.Extensions.Localization.IStringLocalizer<>), typeof(Localization.CoworkeeStringLocalizer<>));
        MudBlazor.Services.ServiceCollectionExtensions.AddLocalizationInterceptor<Localization.CoworkeeMudLocalization>(services);
        services.AddScoped<Data.FileDownloader>();
        services.AddScoped<People.UserCards>();
        services.AddScoped<PermissionStore>();
        services.AddScoped<Features.FeatureStore>();
        services.AddScoped<Theming.ThemeService>();
        services.AddCascadingValue(sp => sp.GetRequiredService<Theming.ThemeService>().DensitySource);
        services.AddScoped<Layout.LayoutPreferences>();
        services.AddScoped<Realtime.IRealtimeConnection, Realtime.SignalRRealtimeConnection>();
        services.AddScoped<Realtime.RealtimeClient>();
        services.AddScoped<Realtime.NotificationCenter>();
        services.AddScoped<BffAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<BffAuthenticationStateProvider>());
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<INavigationContributor, AdminNavigation>();
        services.AddSingleton<INavigationContributor, Navigation.ApiDocsNavigation>();
        services.AddOptions<Navigation.NavigationMenuOptions>();
        services.AddSingleton(new ClientAppConfiguration(Contracts.Settings.CoworkeeAppSettings.Section, AppSettingsTitle, typeof(Contracts.Settings.CoworkeeAppSettings), null, IsAppSettings: true));
        Customization.ComponentReplacementExtensions.AddComponentReplacement(services);
        return services;
    }

    /// <summary>Call between Build and RunAsync: picks the language before the first render, so pages do not rebuild for it.</summary>
    public static Task InitializeCoworkeeClientAsync(this IServiceProvider services) =>
        services.GetRequiredService<Localization.CoworkeeLocalizer>().InitializeAsync(null);

    public const string AppSettingsTitle = "App settings";

    /// <summary>
    /// Lets admins edit another typed section under Settings (the server registers the same type with
    /// AddCoworkeeAppConfiguration); <paramref name="meta"/> tunes the form like any MudExObjectEditForm.
    /// </summary>
    public static IServiceCollection AddCoworkeeAppConfiguration<T>(this IServiceCollection services, string section, string title,
        Action<MudBlazor.Extensions.Components.ObjectEdit.Options.ObjectEditMeta<T>>? meta = null)
        where T : class, new() =>
        services.AddSingleton(new ClientAppConfiguration(section, title, typeof(T), meta));

    /// <summary>
    /// The typed app settings, replacing the default CoworkeeAppSettings (the server registers the same type with AddCoworkeeSettings).
    /// Locked and hidden properties come from the server; <paramref name="meta"/> groups, labels or renders the rest.
    /// </summary>
    public static IServiceCollection AddCoworkeeSettings<T>(this IServiceCollection services,
        Action<MudBlazor.Extensions.Components.ObjectEdit.Options.ObjectEditMeta<T>>? meta = null,
        string section = Contracts.Settings.CoworkeeAppSettings.Section, string title = AppSettingsTitle)
        where T : class, new()
    {
        foreach (var existing in services.Where(d => d.ImplementationInstance is ClientAppConfiguration { IsAppSettings: true }).ToList())
        {
            services.Remove(existing);
        }

        return services.AddSingleton(new ClientAppConfiguration(section, title, typeof(T), meta, IsAppSettings: true));
    }
}

/// <summary>A typed configuration section the settings page offers; <see cref="Meta"/> is an Action&lt;ObjectEditMeta&lt;T&gt;&gt; or null.</summary>
public sealed record ClientAppConfiguration(string Section, string Title, Type Type, object? Meta, bool IsAppSettings = false);

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
        new("Settings", "/admin/settings", MudBlazor.Icons.Material.Outlined.Tune, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: System),
        new("Services", "/admin/services", MudBlazor.Icons.Material.Outlined.Hub, Coworkee.Contracts.Settings.SettingsPermissions.Manage, Group: System, Order: 1, HostOnly: true),
        new("Tenants", "/admin/tenants", MudBlazor.Icons.Material.Outlined.Domain, Coworkee.Contracts.Features.FeaturePermissions.Tenants, Group: System, Order: 2, HostOnly: true),
        new("Editions", "/admin/editions", MudBlazor.Icons.Material.Outlined.WorkspacePremium, Coworkee.Contracts.Features.FeaturePermissions.Editions, Group: System, Order: 3, HostOnly: true),
        new("Themes", "/admin/themes", MudBlazor.Icons.Material.Outlined.Palette, Coworkee.Contracts.Theming.ThemePermissions.Manage, Group: System, Order: 4),
        new("Backups", "/admin/backups", MudBlazor.Icons.Material.Outlined.Backup, Coworkee.Contracts.Backup.BackupPermissions.Manage, Group: System, Order: 5, HostOnly: true),
        new("Mail templates", "/admin/mail/templates", MudBlazor.Icons.Material.Outlined.Email, Coworkee.Contracts.Mailing.MailPermissions.Templates.Manage, Group: Communication),
        new("Mail log", "/admin/mail/log", MudBlazor.Icons.Material.Outlined.Outbox, Coworkee.Contracts.Mailing.MailPermissions.Log.View, Group: Communication, Order: 1),
        new("Languages", "/admin/languages", MudBlazor.Icons.Material.Outlined.Language, Coworkee.Contracts.Localization.LocalizationPermissions.Manage, Group: Localization),
        new("Translations", "/admin/translations", MudBlazor.Icons.Material.Outlined.Translate, Coworkee.Contracts.Localization.LocalizationPermissions.Manage, Group: Localization, Order: 1),
        new("Audit log", "/admin/audit", MudBlazor.Icons.Material.Outlined.History, Coworkee.Contracts.Auditing.AuditPermissions.View, Group: Monitoring),
        new("AI tool calls", "/admin/ai-tool-calls", MudBlazor.Icons.Material.Outlined.ManageSearch, Coworkee.Contracts.Ai.AiPermissions.Audit, Group: Monitoring, Order: 1),
        new("Files", "/files", MudBlazor.Icons.Material.Outlined.FolderOpen, Coworkee.Contracts.Files.FilePermissions.View, Order: -8),
        new("Chat", "/chat", MudBlazor.Icons.Material.Outlined.Chat, Coworkee.Contracts.Chat.ChatPermissions.Use, Order: -9),
        new("Assistant", "/assistant", MudBlazor.Icons.Material.Outlined.AutoAwesome, Coworkee.Contracts.Ai.AiPermissions.Chat, Order: -10),
    ];
}
