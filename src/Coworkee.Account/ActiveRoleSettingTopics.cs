using Coworkee.Contracts.Realtime;
using Coworkee.Domain;
using Coworkee.Settings;

namespace Coworkee.Account;

/// <summary>Switching the setting changes the permissions of everyone, so every client reloads them.</summary>
internal sealed class ActiveRoleSettingTopics : IRealtimeTopicMapper
{
    public IEnumerable<string> TopicsFor(object entity) =>
        entity is SettingValue { Name: ActiveRoleSetting.Name } ? [RealtimeTopics.GlobalPermissions] : [];
}
