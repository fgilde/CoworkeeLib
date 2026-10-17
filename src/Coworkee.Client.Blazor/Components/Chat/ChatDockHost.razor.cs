using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>
/// The mini chats at the bottom right, left of the assistant button: open windows, minimized header bars with their unread count;
/// on phones an open one fills the screen. Part of the layout for users with Chat.Use.
/// </summary>
public partial class ChatDockHost : IAsyncDisposable
{
    private readonly Dictionary<Guid, ChatConversationDto> _chats = [];
    private readonly Dictionary<Guid, int> _unread = [];
    private IAsyncDisposable? _subscription;
    private Guid _me;
    private bool _available;

    [Inject] private ChatDock Dock { get; set; } = null!;

    [Inject] private IChatApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (!Guid.TryParse(user?.FindFirst("sub")?.Value, out _me) || !await Permissions.HasAsync(ChatPermissions.Use))
        {
            return;
        }

        _available = true;
        Dock.Changed += Refresh;
        _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(_me), envelope => InvokeAsync(() => ReceiveAsync(envelope)));
        await Dock.AttachAsync(_me);
    }

    public async ValueTask DisposeAsync()
    {
        Dock.Changed -= Refresh;
        if (_available)
        {
            Dock.Detach();
        }

        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private Guid? Partner(ChatConversationDto chat) => chat.Members.FirstOrDefault(m => m.UserId != _me)?.UserId;

    private void Refresh() => _ = InvokeAsync(async () =>
    {
        foreach (var window in Dock.Windows.Where(w => !_chats.ContainsKey(w.ConversationId)).ToList())
        {
            try
            {
                var chat = await Api.GetChatAsync(window.ConversationId);
                _chats[chat.Id] = chat;
                _unread[chat.Id] = chat.Unread;
            }
            catch (ApiException)
            {
                // left the group or the chat is gone
                await Dock.CloseAsync(window.ConversationId);
            }
        }

        foreach (var window in Dock.Windows.Where(w => !w.Minimized))
        {
            _unread.Remove(window.ConversationId);
        }

        StateHasChanged();
    });

    private Task ReceiveAsync(RealtimeEnvelope envelope)
    {
        if (envelope.Type == ChatEvents.Message && envelope.Payload.Deserialize<ChatMessageDto>(JsonSerializerOptions.Web) is { } message
            && message.FromUserId != _me && Dock.Windows.Any(w => w.ConversationId == message.ConversationId && w.Minimized))
        {
            _unread[message.ConversationId] = _unread.GetValueOrDefault(message.ConversationId) + 1;
            StateHasChanged();
        }
        else if (envelope.Type == ChatEvents.Conversation && envelope.Payload.Deserialize<ChatConversationDto>(JsonSerializerOptions.Web) is { } chat && _chats.ContainsKey(chat.Id))
        {
            if (chat.Members.All(m => m.UserId != _me))
            {
                _chats.Remove(chat.Id);
                return Dock.CloseAsync(chat.Id);
            }

            _chats[chat.Id] = chat;
            StateHasChanged();
        }

        return Task.CompletedTask;
    }
}
