using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Components.Groups;

public static class GroupDetailTabs
{
    /// <summary>Adds a tab after General, Members and Roles on the groups page, shown to users with <paramref name="permission"/> (everyone for <c>null</c>).</summary>
    public static IServiceCollection AddGroupDetailTab<TComponent>(this IServiceCollection services, string title, string? permission = null)
        where TComponent : IComponent =>
        services.AddSingleton(new GroupDetailTab(typeof(TComponent), title, permission));
}
