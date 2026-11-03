using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Coworkee.Domain;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Core.Attributes;

namespace Coworkee.Settings;

/// <summary>
/// A saved setting tells the clients it reaches (global, the organisation, the user), which reload their settings; the event names the setting, not
/// its value. An organisation's setting goes to the organisation of whoever saved it.
/// </summary>
// shortcut: SettingValue has no TenantId, so the realtime interceptor sends to the saver's organisation; a system administrator saving another
// organisation's setting reaches that organisation's clients only at their next load. Give the row a TenantId once that is needed live
[RegisterAs(typeof(IRealtimeTopicMapper), ServiceLifetime = ServiceLifetime.Singleton)]
internal sealed class SettingValueTopics : IRealtimeTopicMapper
{
    public IEnumerable<string> TopicsFor(object entity) => entity switch
    {
        SettingValue { Scope: SettingScope.Global } => [SettingTopics.Global],
        SettingValue { Scope: SettingScope.Tenant } => [SettingTopics.Tenant],
        SettingValue { Scope: SettingScope.User, ScopeKey: { } user } => [RealtimeTopics.User(user)],
        _ => [],
    };
}
