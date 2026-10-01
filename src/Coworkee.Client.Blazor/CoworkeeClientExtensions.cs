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
    ];
}
