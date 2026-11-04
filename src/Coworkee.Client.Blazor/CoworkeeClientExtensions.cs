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
        services.AddHttpClient<Data.IDataTransferApi, Data.DataTransferApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Localization.ILocalizationApi, Localization.LocalizationApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Realtime.IRealtimeWatchApi, Realtime.RealtimeWatchApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<ExtendedAttributes.IExtendedAttributesApi, ExtendedAttributes.ExtendedAttributesApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Chat.IChatApi, Chat.ChatApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Search.IGlobalSearchApi, Search.GlobalSearchApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Social.ISocialApi, Social.SocialApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Workflows.IWorkflowApi, Workflows.WorkflowApi>(client => client.BaseAddress = baseAddress);
        services.AddScoped<Workflows.WorkflowCommands>();
        services.AddScoped<Workflows.WorkflowStates>();
        services.AddHttpClient<Features.IFeaturesApi, Features.FeaturesApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Security.IClientsApi, Security.ClientsApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Security.IApiKeysApi, Security.ApiKeysApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<History.IEntityHistoryApi, History.EntityHistoryApi>(client => client.BaseAddress = baseAddress);
        CommandPalette.CommandPaletteServiceCollectionExtensions.AddCommandPalette(services);
        services.AddHttpClient<Webhooks.IWebhooksApi, Webhooks.WebhooksApi>(client => client.BaseAddress = baseAddress);
        services.AddHttpClient<Retention.IRetentionApi, Retention.RetentionApi>(client => client.BaseAddress = baseAddress);
        services.AddSingleton(new Pages.ProfileTab("API keys", "api-keys", typeof(Pages.ProfileApiKeys), Coworkee.Contracts.Identity.ApiKeyPermissions.Create,
            MudBlazor.Icons.Material.Outlined.Key, Order: 100));
        services.AddHttpClient<Backup.IBackupApi, Backup.BackupApi>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddHttpClient<Attachments.IAttachmentsApi, Attachments.AttachmentsApi>(client =>
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
            client.Timeout = TimeSpan.FromMinutes(11);
        });
        services.AddScoped<Ai.AssistantState>();
        services.AddScoped<Chat.ChatDock>();
        services.AddHttpClient<Ai.IAiConnectionApi, Ai.AiConnectionApi>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        services.AddHttpClient<Mail.IMailConnectionApi, Mail.MailConnectionApi>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        ServiceProxies.ServiceProxyRegistry.AddTo(services, baseAddress, options.ServiceProxies);
        services.AddSingleton<Localization.CoworkeeLocalizer>();
        services.AddSingleton(typeof(Microsoft.Extensions.Localization.IStringLocalizer<>), typeof(Localization.CoworkeeStringLocalizer<>));
        MudBlazor.Services.ServiceCollectionExtensions.AddLocalizationInterceptor<Localization.CoworkeeMudLocalization>(services);
        services.AddScoped<Data.FileDownloader>();
        services.AddScoped<Data.ITableViewStore, Data.TableViews>();
        services.AddScoped<People.UserCards>();
        services.AddScoped<People.Presence>();
        services.AddScoped<Navigation.NavigationBadges>();
        services.AddScoped<PermissionStore>();
        services.AddScoped<Features.FeatureStore>();
        services.AddScoped<Theming.ThemeService>();
        services.AddCascadingValue(sp => sp.GetRequiredService<Theming.ThemeService>().DensitySource);
        services.AddScoped<Layout.LayoutPreferences>();
        services.AddScoped<Layout.PwaService>();
        services.AddScoped<Realtime.IRealtimeConnection, Realtime.SignalRRealtimeConnection>();
        services.AddScoped<Realtime.RealtimeClient>();
        services.AddScoped<Realtime.NotificationCenter>();
        services.AddScoped<Components.Groups.GroupScopeState>();
        services.AddScoped<Data.DataTransferWatcher>();
        services.AddScoped<BffAuthenticationStateProvider>();
        services.AddScoped<CurrentUserInfo>();
        services.AddScoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<BffAuthenticationStateProvider>());
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        foreach (var page in Navigation.LibraryPages.All)
        {
            services.AddSingleton(page);
        }

        services.AddSingleton<Navigation.CoworkeePages>();
        services.AddScoped<Navigation.LinkNavigation>();
        services.AddSingleton<INavigationContributor, AdminNavigation>();
        services.AddSingleton<INavigationContributor, Navigation.ApiDocsNavigation>();
        services.AddOptions<Navigation.NavigationMenuOptions>();
        services.AddScoped(typeof(MudBlazor.Extensions.Components.ObjectEdit.IObjectMetaConfiguration<>), typeof(Components.SettingsItemMeta<>));
        services.AddScoped<MudBlazor.Extensions.Components.ObjectEdit.IObjectMetaConfiguration<Theming.CoworkeeTheme>, Theming.CoworkeeThemeEditMeta>();
        services.AddSingleton(new ClientAppConfiguration(Contracts.Settings.CoworkeeAppSettings.Section, AppSettingsTitle, typeof(Contracts.Settings.CoworkeeAppSettings), null, IsAppSettings: true));
        Customization.ComponentReplacementExtensions.AddComponentReplacement(services);
        services.Configure<Customization.ComponentReplacementOptions>(o => o.ReplaceGeneric(typeof(MudBlazor.Extensions.Components.ObjectEdit.MudExObjectEditDialog<>), typeof(Components.SettingsItemDialog<>)));
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
