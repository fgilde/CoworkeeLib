using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>A tab of the admin user page added by a module or the app; <see cref="Title"/> is translated, the component gets <c>[Parameter] Guid UserId</c>.</summary>
public sealed record UserDetailTab(Type Component, string Title, string? Permission, string? Icon = null, int Order = 0);

public static class UserDetailTabs
{
    /// <summary>
    /// Adds a tab after General, Roles, Groups, Permissions and Sessions on the admin user page, shown to users with <paramref name="permission"/>
    /// (everyone who may see the user for <c>null</c>); added tabs follow each other by <paramref name="order"/>.
    /// </summary>
    public static IServiceCollection AddUserDetailTab<TComponent>(this IServiceCollection services, string title, string? permission = null, string? icon = null, int order = 0)
        where TComponent : IComponent =>
        services.AddSingleton(new UserDetailTab(typeof(TComponent), title, permission, icon, order));
}
