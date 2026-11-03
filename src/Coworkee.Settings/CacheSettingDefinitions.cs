using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class CacheSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Cache", "Cache")
            .Add(CacheSettings.DurationMinutes, "Cache duration (minutes)", SettingType.Int, [SettingScope.Global, SettingScope.Tenant], "5",
                description: "How long a cached answer lives when its query names no time of its own.")
            .Add(CacheSettings.RefreshAfterMinutes, "Refresh after (minutes)", SettingType.Int, [SettingScope.Global, SettingScope.Tenant], "0",
                description: "An answer older than this is shown at once and loaded again in the background; 0 turns it off.")
            .Add(CacheSettings.RefreshInBackground, "Refresh in the background", SettingType.Bool, [SettingScope.Global, SettingScope.Tenant], "true",
                description: "Off: cached answers are never loaded again in the background, also where a query asks for it.");
}
