using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class TableSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Appearance", "Appearance")
            .Add(TableSettings.Views, "Saved table views", SettingType.Text, [SettingScope.User],
                description: "Columns, grouping, sorting and filters the user saved per table.")
            .Add(TableSettings.DefaultPageMode, "Table rows", SettingType.Choice, [SettingScope.Global, SettingScope.User], TableSettings.Pager, visibleToClient: true,
                description: "Pages with a pager, or endless scrolling that loads more rows while scrolling.", choices: [TableSettings.Pager, TableSettings.EndlessScroll])
            .Add(TableSettings.ClientEntityModes, "Search on the client or the server", SettingType.Text, [SettingScope.User],
                description: "Per table with a local copy: whether searching and filtering run in the browser or on the server.");
}
