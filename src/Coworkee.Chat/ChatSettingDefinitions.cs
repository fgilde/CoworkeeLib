using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Chat;

internal sealed class ChatSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Chat", "Chat")
            .Add(ChatSettings.MaxAttachmentMb, "Largest chat attachment (MB)", SettingType.Int, [SettingScope.Global], ChatLimits.DefaultAttachmentMb.ToString(System.Globalization.CultureInfo.InvariantCulture),
                visibleToClient: true, description: "Files above this size cannot be sent in the chat.");
}
