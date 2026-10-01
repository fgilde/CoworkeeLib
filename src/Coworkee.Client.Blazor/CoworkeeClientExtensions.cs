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
        services.AddHttpClient<ICoworkeeApi, CoworkeeApi>(client => client.BaseAddress = baseAddress);
        services.AddScoped<PermissionStore>();
        services.AddScoped<Theming.ThemeService>();
        services.AddScoped<Realtime.IRealtimeConnection, Realtime.SignalRRealtimeConnection>();
        services.AddScoped<Realtime.RealtimeClient>();
        services.AddScoped<BffAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<BffAuthenticationStateProvider>());
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<INavigationContributor, AdminNavigation>();
        return services;
    }
}

internal sealed class AdminNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Users", "/admin/users", MudBlazor.Icons.Material.Outlined.Person, Coworkee.Contracts.Identity.IdentityPermissions.Users.View),
        new("Groups", "/admin/groups", MudBlazor.Icons.Material.Outlined.Groups, Coworkee.Contracts.Identity.IdentityPermissions.Groups.View),
        new("Roles", "/admin/roles", MudBlazor.Icons.Material.Outlined.Shield, Coworkee.Contracts.Identity.IdentityPermissions.Roles.View),
        new("Settings", "/admin/settings", MudBlazor.Icons.Material.Outlined.Tune, Coworkee.Contracts.Settings.SettingsPermissions.Manage),
        new("Mail templates", "/admin/mail/templates", MudBlazor.Icons.Material.Outlined.Email, Coworkee.Contracts.Mailing.MailPermissions.Templates.Manage),
        new("Mail log", "/admin/mail/log", MudBlazor.Icons.Material.Outlined.Outbox, Coworkee.Contracts.Mailing.MailPermissions.Log.View),
        new("Jobs", "/admin/jobs", MudBlazor.Icons.Material.Outlined.Schedule, Coworkee.Contracts.Jobs.JobsPermissions.View, ForceLoad: true),
        new("Themes", "/admin/themes", MudBlazor.Icons.Material.Outlined.Palette, Coworkee.Contracts.Theming.ThemePermissions.Manage),
        new("Audit log", "/admin/audit", MudBlazor.Icons.Material.Outlined.History, Coworkee.Contracts.Auditing.AuditPermissions.View),
        new("My settings", "/settings", MudBlazor.Icons.Material.Outlined.ManageAccounts),
    ];
}
