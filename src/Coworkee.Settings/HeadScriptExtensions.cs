using Coworkee.Application.Authorization;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

public static class HeadScriptExtensions
{
    /// <summary>
    /// Turns the head script on: the settings <see cref="HeadScriptSettings"/> and the permission <see cref="SettingsPermissions.HeadScript"/>.
    /// With <paramref name="perOrganisation"/> every organisation may set its own script for its users; otherwise only the system organisation sets one for all.
    /// </summary>
    public static IServiceCollection AddCoworkeeHeadScript(this IServiceCollection services, bool perOrganisation = false)
    {
        var definitions = new HeadScriptDefinitions(perOrganisation);
        services.AddSingleton<ISettingDefinitionContributor>(definitions);
        services.AddSingleton<IPermissionDefinitionContributor>(definitions);
        return services;
    }
}
