using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.People;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// A new chat message shows a snackbar with a link to the conversation, unless that conversation is open; the navigation shows the
/// unread count on "/chat", live in every tab.
/// </summary>
public partial class ChatNotifier : IAsyncDisposable
{
    private const int Preview = 80;
    private const string ChatHref = "/chat";
    private IAsyncDisposable? _subscription;
    private Guid _me;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Inject] private IChatApi Chat { get; set; } = null!;

    [Inject] private Navigation.NavigationBadges Badges { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (Guid.TryParse(user?.FindFirst("sub")?.Value, out _me) && await Permissions.HasAsync(ChatPermissions.Use))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(_me), envelope => InvokeAsync(() => ReceiveAsync(envelope)));
            await CountAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private async Task CountAsync()
    {
        try
        {
            Badges.Set(Links.Href(ChatHref)!, await Chat.GetUnreadCountAsync());
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
        }
    }

    private async Task ReceiveAsync(RealtimeEnvelope envelope)
    {
        if (envelope.Type == ChatEvents.Read && envelope.Payload.Deserialize<ChatReadDto>(JsonSerializerOptions.Web)?.ReaderId == _me)
        {
            await CountAsync();
            return;
        }

        if (envelope.Type == ChatEvents.Message && envelope.Payload.Deserialize<ChatMessageDto>(JsonSerializerOptions.Web)?.ToUserId == _me)
        {
            await CountAsync();
        }

        if (envelope.Type != ChatEvents.Message || envelope.Payload.Deserialize<ChatMessageDto>(JsonSerializerOptions.Web) is not { } message
            || message.FromUserId == _me || IsOpen(message.FromUserId))
        {
            return;
        }

        var name = (await Cards.GetAsync(message.FromUserId))?.Name ?? L["Someone"];
        var text = message.Text.Length > Preview ? message.Text[..Preview] + "…" : message.Text;
        var link = Links.Href($"/chat?with={message.FromUserId}")!;
        Snackbar.Add(L["{0}: {1}", name, text], Severity.Info, options =>
        {
            options.Action = L["Open chat"];
            options.ActionColor = Color.Primary;
            options.OnClick = _ =>
            {
                Nav.NavigateTo(link);
                return Task.CompletedTask;
            };
        });
    }

    private bool IsOpen(Guid contact)
    {
        var uri = new Uri(Nav.Uri);
        return uri.AbsolutePath.TrimEnd('/').EndsWith("/chat", StringComparison.OrdinalIgnoreCase)
            && uri.Query.Contains($"with={contact}", StringComparison.OrdinalIgnoreCase);
    }
}
