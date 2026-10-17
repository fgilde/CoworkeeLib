using System.Globalization;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Contracts.Chat;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>One message: formatted text with mentions, the answered message, files and pictures, reactions, read marks and its actions.</summary>
public partial class ChatMessageView
{
    public static readonly string[] QuickReactions = ["👍", "❤️", "😂", "🎉", "😮", "😢"];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private CoworkeePages Links { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Parameter, EditorRequired] public ChatMessageDto Message { get; set; } = null!;

    [Parameter, EditorRequired] public ChatConversationDto Conversation { get; set; } = null!;

    [Parameter] public Guid Me { get; set; }

    [Parameter] public EventCallback<ChatMessageDto> Reply { get; set; }

    [Parameter] public EventCallback<ChatMessageDto> Edit { get; set; }

    [Parameter] public EventCallback<ChatMessageDto> Delete { get; set; }

    [Parameter] public EventCallback<string> React { get; set; }

    private bool Mine => Message.FromUserId == Me;

    private bool ShowSender => Conversation.IsGroup;

    private string SenderName => Conversation.Members.FirstOrDefault(m => m.UserId == Message.FromUserId)?.Name ?? string.Empty;

    // the others whose read mark is past the message
    private List<string> SeenBy =>
        [.. Conversation.Members.Where(m => m.UserId != Me && m.LastReadAt >= Message.SentAt).Select(m => m.Name)];

    public static string Url(ChatAttachmentDto file) => $"api/v1/chat/attachments/{file.Id}";

    private static string Preview(ChatAttachmentDto file) => file.HasThumbnail ? $"{Url(file)}/thumbnail" : Url(file);

    private Task OpenAsync(ChatAttachmentDto file) => FilePreviewDialog.ShowAsync(Dialogs, Url(file), file.ContentType, file.Name);

    private static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0 / 1024.0:0.#} MB"),
    };
}
