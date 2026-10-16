using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class EditorSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Appearance", "Appearance")
            .Add(EditorSettings.RichTextEditor, "Rich text editor", SettingType.Choice, [SettingScope.Global], EditorSettings.TipTap, visibleToClient: true,
                description: "The editor of formatted texts such as mail templates. Code and HTML source keep the code editor.",
                choices: [EditorSettings.TipTap, EditorSettings.Quill]);
}
