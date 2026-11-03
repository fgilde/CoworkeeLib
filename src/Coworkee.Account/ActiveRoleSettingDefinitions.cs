using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Account;

internal sealed class ActiveRoleSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Security", "Sign-in security")
            .Add(ActiveRoleSetting.Name, "Act within one role", SettingType.Bool, [SettingScope.Global], "true",
                description: "Users with several roles choose one in the app bar and then have only its permissions; off, they always have all of them.");
}
