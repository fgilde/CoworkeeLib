using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Account;

internal sealed class AccountSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Account", "Accounts")
            .Add(AccountSettings.AllowRegistration, "Allow self registration", SettingType.Bool, [SettingScope.Global], "false",
                description: "Shows the registration wizard on the sign-in page and lets external sign-ins create accounts.")
            .Add(AccountSettings.RegistrationRequiresActivation, "New accounts need activation", SettingType.Bool, [SettingScope.Global], "true",
                description: "New accounts stay inactive until an administrator activates them; administrators get a notification.")
            .Add(AccountSettings.RegistrationRequiresEmailConfirmation, "New accounts confirm their email", SettingType.Bool, [SettingScope.Global], "true",
                description: "Signing in waits until the link in the confirmation mail was opened.");
}
