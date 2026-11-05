using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class DialogSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Dialogs", "Dialogs", page: "/admin/dialog-options")
            .Add(DialogSettings.Options, "Dialog options", SettingType.Text, [SettingScope.Global], visibleToClient: true,
                description: "The options of the dialogs as JSON per kind; kinds it does not name keep the app's options. Applies only when the app lets administrators change them.");
}
