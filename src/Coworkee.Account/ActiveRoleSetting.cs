using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Coworkee.Domain;
using Coworkee.Identity.Roles;
using Coworkee.Settings;

namespace Coworkee.Account;

/// <summary>The system setting behind <see cref="IActiveRoleSwitch"/>; on by default.</summary>
internal sealed class ActiveRoleSetting(ISettingProvider settings) : IActiveRoleSwitch
{
    public const string Name = "Account.ActiveRole";

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => settings.GetAsync<bool>(Name, cancellationToken);
}

internal sealed class ActiveRoleSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Security", "Sign-in security")
            .Add(ActiveRoleSetting.Name, "Act within one role", SettingType.Bool, [SettingScope.Global], "true",
                description: "Users with several roles choose one in the app bar and then have only its permissions; off, they always have all of them.");
}

/// <summary>Switching the setting changes the permissions of everyone, so every client reloads them.</summary>
internal sealed class ActiveRoleSettingTopics : IRealtimeTopicMapper
{
    public IEnumerable<string> TopicsFor(object entity) =>
        entity is SettingValue { Name: ActiveRoleSetting.Name } ? [RealtimeTopics.GlobalPermissions] : [];
}
