using Coworkee.Contracts.Settings;
using Coworkee.Domain;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Core.Attributes;

namespace Coworkee.Theming;

/// <summary>
/// A changed theme or organisation default reaches every client of the organisation (built-in themes: every client) through the settings topics, so
/// SettingsLiveUpdates reloads the theme at once; <see cref="RealtimeAttribute"/> on <see cref="ThemeDefinition"/> only tells the theme managers.
/// </summary>
[RegisterAs(typeof(IRealtimeTopicMapper), ServiceLifetime = ServiceLifetime.Singleton)]
internal sealed class ThemeTopics : IRealtimeTopicMapper
{
    public IEnumerable<string> TopicsFor(object entity) => entity switch
    {
        ThemeDefinition { TenantId: null } => [SettingTopics.Global],
        ThemeDefinition or TenantTheme => [SettingTopics.Tenant],
        _ => [],
    };
}
