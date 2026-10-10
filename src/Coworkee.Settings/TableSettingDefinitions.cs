using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class TableSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Appearance", "Appearance")
            .Add(TableSettings.Views, "Saved table views", SettingType.Text, [SettingScope.User],
                description: "Columns, grouping, sorting and filters the user saved per table.");
}
