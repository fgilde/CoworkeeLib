using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Components.Chat;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>All chats of the user: direct and group chats with unread counts, one open with its members, search and group settings.</summary>
public partial class Chat : IAsyncDisposable
{
    private IReadOnlyList<ChatConversationDto> _chats = [];
    private ChatConversationDto? _chat;
    private string? _filter;
    private string? _search;
    private bool _searching;
    private Guid _me;
    private IAsyncDisposable? _subscription;

    [Inject] private IChatApi Api { get; set; } = null!;

    [Inject] private ChatDock Dock { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    /// <summary>The chat to open, e.g. from a notification.</summary>
    [SupplyParameterFromQuery(Name = "c")] public Guid? Conversation { get; set; }

    /// <summary>The person whose direct chat to open; it is created on first use.</summary>
    [SupplyParameterFromQuery(Name = "with")] public Guid? With { get; set; }

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    private IEnumerable<ChatConversationDto> Chats =>
        string.IsNullOrWhiteSpace(_filter) ? _chats : _chats.Where(c => c.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase));

    private bool IsOwner => _chat?.Members.Any(m => m.UserId == _me && m.IsOwner) == true;

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (Guid.TryParse(user?.FindFirst("sub")?.Value, out _me))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(_me), envelope => InvokeAsync(() => ReceiveAsync(envelope)));
        }

        await Snackbar.RunAsync(LoadAsync);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (With is { } with && Partner(_chat) != with)
        {
            await Snackbar.RunAsync(async () => await ShowAsync(await Api.OpenDirectAsync(with)));
        }
        else if (Conversation is { } id && _chat?.Id != id)
        {
            await Snackbar.RunAsync(async () => await ShowAsync(_chats.FirstOrDefault(c => c.Id == id) ?? await Api.GetChatAsync(id)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private Guid? Partner(ChatConversationDto? chat) => chat is { IsGroup: false } ? chat.Members.FirstOrDefault(m => m.UserId != _me)?.UserId : null;

    private async Task LoadAsync()
    {
        _chats = await Api.GetChatsAsync();
        _chat = _chat is null ? null : _chats.FirstOrDefault(c => c.Id == _chat.Id) ?? _chat;
    }

    private Task SelectAsync(ChatConversationDto chat)
    {
        Nav.NavigateTo(Nav.GetUriWithQueryParameters(new Dictionary<string, object?> { ["c"] = chat.Id, ["with"] = null }), replace: true);
        return ShowAsync(chat);
    }

    private async Task ShowAsync(ChatConversationDto chat)
    {
        _chat = chat;
        _search = null;
        _searching = false;
        if (_chats.All(c => c.Id != chat.Id))
        {
            await LoadAsync();
        }

        StateHasChanged();
    }

    private async Task NewChatAsync()
    {
        if (await Dialogs.PickPeopleAsync(L["Chat with…"]) is { UserIds: [var userId] })
        {
            await Snackbar.RunAsync(async () => await SelectAsync(await Api.OpenDirectAsync(userId)));
        }
    }

    private async Task NewGroupAsync()
    {
        if (await Dialogs.PickPeopleAsync(L["New group"], multiple: true, askName: true) is { Name: { Length: > 0 } name } pick)
        {
            await Snackbar.RunAsync(async () => await SelectAsync(await Api.CreateGroupAsync(name, pick.UserIds)));
        }
    }

    private async Task RenameAsync()
    {
        var name = await Dialogs.PromptAsync(L["Rename"], L["Group name"], _chat!.Name, L["Save"], L["Cancel"], canConfirm: n => !string.IsNullOrWhiteSpace(n));
        if (!string.IsNullOrWhiteSpace(name))
        {
            await Snackbar.RunAsync(async () => _chat = await Api.RenameAsync(_chat.Id, name.Trim()));
        }
    }

    private async Task AddMembersAsync()
    {
        if (await Dialogs.PickPeopleAsync(L["Add people"], multiple: true, exclude: [.. _chat!.Members.Select(m => m.UserId)]) is { } pick)
        {
            await Snackbar.RunAsync(async () => _chat = await Api.AddMembersAsync(_chat.Id, pick.UserIds));
        }
    }

    private async Task RemoveAsync(ChatMemberDto member)
    {
        if (await Dialogs.ConfirmAsync(L["Remove"], L["Remove {0} from the group?", member.Name], L["Remove"], L["Cancel"], Icons.Material.Outlined.PersonRemove))
        {
            await Snackbar.RunAsync(() => Api.RemoveMemberAsync(_chat!.Id, member.UserId));
        }
    }

    private async Task LeaveAsync()
    {
        if (await Dialogs.ConfirmAsync(L["Leave group"], L["Leave {0}? You will no longer see its messages.", _chat!.Name], L["Leave"], L["Cancel"], Icons.Material.Outlined.Logout)
            && await Snackbar.RunAsync(() => Api.RemoveMemberAsync(_chat.Id, _me)))
        {
            _chat = null;
            await Snackbar.RunAsync(LoadAsync);
        }
    }

    private void ToggleSearch()
    {
        _searching = !_searching;
        _search = null;
    }

    private void SearchAsync(string? text) => _search = text;

    // counts and last messages follow every chat event
    private async Task ReceiveAsync(RealtimeEnvelope envelope)
    {
        if (envelope.Type is not (ChatEvents.Message or ChatEvents.Read or ChatEvents.Conversation))
        {
            return;
        }

        if (envelope.Type == ChatEvents.Conversation && envelope.Payload.Deserialize<ChatConversationDto>(JsonSerializerOptions.Web) is { } changed && changed.Id == _chat?.Id)
        {
            _chat = changed.Members.Any(m => m.UserId == _me) ? changed : null;
        }

        try
        {
            await LoadAsync();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
        }

        StateHasChanged();
    }
}
