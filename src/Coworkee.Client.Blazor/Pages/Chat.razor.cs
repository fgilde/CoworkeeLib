using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

public partial class Chat : IAsyncDisposable
{
    private IReadOnlyList<ChatContactDto> _contacts = [];
    private List<ChatMessageDto> _messages = [];
    private ChatContactDto? _contact;
    private string? _filter;
    private string? _text;
    private Guid _me;
    private IAsyncDisposable? _subscription;

    [Inject] private IChatApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    private IEnumerable<ChatContactDto> Contacts =>
        string.IsNullOrWhiteSpace(_filter) ? _contacts : _contacts.Where(c => c.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (Guid.TryParse(user?.FindFirst("sub")?.Value, out _me))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(_me), envelope => InvokeAsync(() => ReceiveAsync(envelope)));
        }

        await Snackbar.RunAsync(LoadContactsAsync);
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private async Task OpenAsync(ChatContactDto? contact)
    {
        _contact = contact;
        _messages = [];
        if (contact is not null)
        {
            await Snackbar.RunAsync(async () =>
            {
                _messages = [.. await Api.GetConversationAsync(contact.UserId)];
                await MarkReadAsync(contact);
            });
        }
    }

    private async Task SendAsync()
    {
        if (_contact is null || string.IsNullOrWhiteSpace(_text))
        {
            return;
        }

        var text = _text;
        _text = null;
        if (!await Snackbar.RunAsync(async () => Add(await Api.SendAsync(_contact.UserId, text))))
        {
            _text = text;
        }
    }

    private async Task KeyDownAsync(KeyboardEventArgs args)
    {
        if (args is { Key: "Enter", ShiftKey: false })
        {
            await SendAsync();
        }
    }

    private async Task ReceiveAsync(RealtimeEnvelope envelope)
    {
        if (envelope.Type != ChatEvents.Message || envelope.Payload.Deserialize<ChatMessageDto>(JsonSerializerOptions.Web) is not { } message)
        {
            return;
        }

        var other = message.FromUserId == _me ? message.ToUserId : message.FromUserId;
        if (_contact?.UserId == other)
        {
            Add(message);
            if (message.FromUserId == other)
            {
                await MarkReadAsync(_contact);
            }
        }

        await LoadContactsAsync();
        StateHasChanged();
    }

    private void Add(ChatMessageDto message)
    {
        if (_messages.All(m => m.Id != message.Id))
        {
            _messages.Add(message);
        }
    }

    private async Task MarkReadAsync(ChatContactDto contact)
    {
        if (contact.Unread > 0 || _messages.Any(m => m.FromUserId == contact.UserId && m.ReadAt is null))
        {
            await Api.MarkReadAsync(contact.UserId);
            await LoadContactsAsync();
        }
    }

    private async Task LoadContactsAsync()
    {
        _contacts = await Api.GetContactsAsync();
        _contact = _contact is null ? null : _contacts.FirstOrDefault(c => c.UserId == _contact.UserId) ?? _contact;
    }

}
