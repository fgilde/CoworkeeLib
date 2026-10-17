using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>
/// The messages of one chat with its composer, live: new, changed and deleted messages, reactions, read marks and who is writing.
/// Used by the chat page and the mini chats; marks the chat read while it shows.
/// </summary>
public partial class ChatThread : IAsyncDisposable
{
    private const int PageSize = 50;
    private static readonly TimeSpan TypingShows = TimeSpan.FromSeconds(5);

    private readonly Dictionary<Guid, DateTimeOffset> _typing = [];
    private List<ChatMessageDto> _messages = [];
    private ChatConversationDto? _conversation;
    private ChatMessageDto? _replyTo;
    private ChatMessageDto? _editing;
    private ElementReference _list;
    private IAsyncDisposable? _subscription;
    private Guid _me;
    private Guid _loaded;
    private string? _searched;
    private bool _more;
    private bool _scroll;

    [Inject] private IChatApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter, EditorRequired] public Guid ConversationId { get; set; }

    /// <summary>The narrow form of a mini chat.</summary>
    [Parameter] public bool Compact { get; set; }

    /// <summary>Shows only the messages containing every word.</summary>
    [Parameter] public string? Search { get; set; }

    [Parameter] public string? Height { get; set; }

    public ChatConversationDto? Conversation => _conversation;

    private List<string> Typing =>
        [.. _typing.Where(t => DateTimeOffset.UtcNow - t.Value < TypingShows).Select(t => _conversation?.Members.FirstOrDefault(m => m.UserId == t.Key)?.Name ?? string.Empty)];

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (Guid.TryParse(user?.FindFirst("sub")?.Value, out _me))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(_me), envelope => InvokeAsync(() => ReceiveAsync(envelope)));
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loaded != ConversationId || _searched != Search)
        {
            _loaded = ConversationId;
            _searched = Search;
            _replyTo = null;
            _editing = null;
            _typing.Clear();
            await Snackbar.RunAsync(LoadAsync);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scroll)
        {
            _scroll = false;
            try
            {
                var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/chat.js");
                await module.InvokeVoidAsync("scrollToEnd", _list);
            }
            catch (JSException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private async Task LoadAsync()
    {
        _conversation = await Api.GetChatAsync(ConversationId);
        var page = await Api.GetMessagesAsync(ConversationId, null, string.IsNullOrWhiteSpace(Search) ? null : Search);
        _messages = [.. page];
        _more = page.Count >= PageSize;
        _scroll = true;
        await MarkReadAsync();
    }

    private Task OlderAsync() => Snackbar.RunAsync(async () =>
    {
        var page = await Api.GetMessagesAsync(ConversationId, _messages.FirstOrDefault()?.SentAt);
        _messages.InsertRange(0, page.Where(m => _messages.All(x => x.Id != m.Id)));
        _more = page.Count >= PageSize;
    });

    private void Arrived(ChatMessageDto message)
    {
        Upsert(message);
        _scroll = true;
    }

    private void Upsert(ChatMessageDto message)
    {
        var index = _messages.FindIndex(m => m.Id == message.Id);
        if (index >= 0)
        {
            _messages[index] = message;
        }
        else if (string.IsNullOrWhiteSpace(Search))
        {
            _messages.Add(message);
            _scroll = true;
        }
    }

    private async Task DeleteAsync(ChatMessageDto message)
    {
        if (await Dialogs.ConfirmAsync(L["Delete message"], L["Delete this message for everyone?"], L["Delete"], L["Cancel"], Icons.Material.Outlined.Delete))
        {
            await Snackbar.RunAsync(() => Api.DeleteAsync(message.Id));
        }
    }

    private Task ReactAsync(ChatMessageDto message, string emoji) => Snackbar.RunAsync(async () => Upsert(await Api.ReactAsync(message.Id, emoji)));

    private async Task ReceiveAsync(RealtimeEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case ChatEvents.Message when envelope.Payload.Deserialize<ChatMessageDto>(JsonSerializerOptions.Web) is { } message && message.ConversationId == ConversationId:
                // records are only resolved with the reader's rights: such a message is loaded again
                Upsert(message.HasEntityMentions && !message.Deleted ? await Api.GetMessageAsync(message.Id) : message);
                _typing.Remove(message.FromUserId);
                if (message.FromUserId != _me)
                {
                    await MarkReadAsync();
                }

                break;
            case ChatEvents.Read when envelope.Payload.Deserialize<ChatReadDto>(JsonSerializerOptions.Web) is { } read && read.ConversationId == ConversationId && _conversation is not null:
                _conversation = _conversation with
                {
                    Members = [.. _conversation.Members.Select(m => m.UserId == read.ReaderId ? m with { LastReadAt = read.ReadAt } : m)],
                };
                break;
            case ChatEvents.Typing when envelope.Payload.Deserialize<ChatTypingDto>(JsonSerializerOptions.Web) is { } typing && typing.ConversationId == ConversationId:
                _typing[typing.UserId] = DateTimeOffset.UtcNow;
                _ = ForgetTypingAsync();
                break;
            case ChatEvents.Conversation when envelope.Payload.Deserialize<ChatConversationDto>(JsonSerializerOptions.Web) is { } conversation && conversation.Id == ConversationId:
                _conversation = conversation.Members.Any(m => m.UserId == _me) ? conversation with { Unread = 0 } : _conversation;
                break;
            default:
                return;
        }

        StateHasChanged();
    }

    private async Task ForgetTypingAsync()
    {
        await Task.Delay(TypingShows);
        await InvokeAsync(StateHasChanged);
    }

    private async Task MarkReadAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Search) && _messages.Any(m => m.FromUserId != _me))
            {
                await Api.MarkChatReadAsync(ConversationId);
            }
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
        }
    }
}
