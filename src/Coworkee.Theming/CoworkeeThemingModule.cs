using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Versioning;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Theming;

[DependsOn(typeof(CoworkeeSettingsModule))]
public sealed class CoworkeeThemingModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeThemingModule).Assembly);
        services.AddSingleton<ThemeModelContributor>();
        services.AddSingleton<IModelContributor>(provider => provider.GetRequiredService<ThemeModelContributor>());
        services.AddSingleton<IVersionedTypeContributor>(provider => provider.GetRequiredService<ThemeModelContributor>());
        services.AddSingleton<IPermissionDefinitionContributor, ThemePermissionDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, ThemeSettingDefinitions>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var themes = app.MapGroup("/api/v1/themes").WithTags("Themes");
        themes.MapGet("/current", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetCurrentTheme(), ct).ToHttpResult()).AllowAnonymous();
        var managed = themes.MapGroup(string.Empty).RequireAuthorization();
        managed.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetThemes(), ct).ToHttpResult());
        managed.MapPost("/", (ThemeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateTheme(body), ct).ToHttpResult());
        managed.MapPut("/{id:guid}", (Guid id, ThemeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateTheme(id, body), ct).ToHttpResult());
        managed.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteTheme(id), ct).ToHttpResult());
        managed.MapPost("/{id:guid}/default", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetDefaultTheme(id), ct).ToHttpResult());
    }
}

internal sealed class ThemePermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(ThemePermissions.GroupName, "Themes").Add(ThemePermissions.Manage, "Manage themes");
}

internal sealed class ThemeSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Appearance", "Appearance")
            .Add(ThemeSettings.Mode, "Color mode", SettingType.Choice, [SettingScope.User], "system", visibleToClient: true, choices: ["system", "light", "dark"])
            .Add(ThemeSettings.ThemeId, "Theme", SettingType.String, [SettingScope.User], visibleToClient: true);
}
