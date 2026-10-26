using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>A tab of the group details added by the app; <see cref="Title"/> is translated, the component gets <c>[Parameter] Guid GroupId</c>.</summary>
public sealed record GroupDetailTab(Type Component, string Title, string? Permission);

public static class GroupDetailTabs
{
    /// <summary>Adds a tab after General, Members and Roles on the groups page, shown to users with <paramref name="permission"/> (everyone for <c>null</c>).</summary>
    public static IServiceCollection AddGroupDetailTab<TComponent>(this IServiceCollection services, string title, string? permission = null)
        where TComponent : IComponent =>
        services.AddSingleton(new GroupDetailTab(typeof(TComponent), title, permission));
}
