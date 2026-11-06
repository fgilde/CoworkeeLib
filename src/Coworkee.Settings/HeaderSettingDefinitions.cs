using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class HeaderSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Header", "Header", page: "/admin/header")
            .Add(HeaderSettings.Items, "Header", SettingType.Text, [SettingScope.Global], visibleToClient: true,
                description: "Order and place of the app bar items as JSON; items it does not name keep their default place.")
            .Add(HeaderSettings.Position, "Position of the app bar", SettingType.Text, [SettingScope.Global], visibleToClient: true,
                description: "Top, Bottom, Left or Right.")
            .Add(HeaderSettings.SecondPosition, "Position of the second app bar", SettingType.Text, [SettingScope.Global], visibleToClient: true,
                description: "Top, Bottom, Left or Right; empty: no second app bar.");
}
