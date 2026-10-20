using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.People;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The picture of a user, or the initials; updates when the user changes it.</summary>
public partial class UserAvatar : IDisposable
{
    private UserCardDto? _card;
    private bool _online;
    private bool _offerChat;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Inject] private Presence Presence { get; set; } = null!;

    [Inject] private IServiceProvider Services { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [CascadingParameter] private Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState>? AuthenticationState { get; set; }

    [Parameter, EditorRequired] public Guid UserId { get; set; }

    [Parameter] public Size Size { get; set; } = Size.Medium;

    /// <summary>A size in pixels beyond the MudBlazor sizes, e.g. 160 on the profile page.</summary>
    [Parameter] public int? Pixels { get; set; }

    [Parameter] public bool ShowTooltip { get; set; }

    /// <summary>A green dot while the user is online.</summary>
    [Parameter] public bool ShowPresence { get; set; } = true;

    /// <summary>A click offers "Chat" (the mini chat) and the profile, for other people while the user may chat; off inside lists that select on click.</summary>
    [Parameter] public bool OfferChat { get; set; } = true;

    private string? StyleText => Pixels is { } px ? $"width:{px}px;height:{px}px;font-size:{px / 2.5}px" : null;

    private string Initials => _card is null
        ? "?"
        : string.Concat(_card.Name.Split(' ', '@', '.').Where(p => p.Length > 0).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    protected override void OnInitialized()
    {
        Cards.Changed += Reload;
        Presence.Changed += PresenceChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_card?.Id != UserId)
        {
            _card = await Cards.GetAsync(UserId);
        }

        _online = ShowPresence && await Presence.IsOnlineAsync(UserId);
        _offerChat = OfferChat && _card is not null && await MayChatAsync();
    }

    private async Task<bool> MayChatAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        return ClientUser.From(user).TryGetUserId(out var me) && me != UserId && Services.GetService<ChatDock>() is not null
            && Services.GetService<Security.PermissionStore>() is { } permissions && await permissions.HasAsync(Contracts.Chat.ChatPermissions.Use);
    }

    private Task ChatAsync() => Services.GetRequiredService<ChatDock>().OpenWithAsync(UserId);

    public void Dispose()
    {
        Cards.Changed -= Reload;
        Presence.Changed -= PresenceChanged;
    }

    private void PresenceChanged(Guid userId)
    {
        if (userId == UserId && ShowPresence)
        {
            _ = InvokeAsync(async () =>
            {
                _online = await Presence.IsOnlineAsync(UserId);
                StateHasChanged();
            });
        }
    }

    private void Reload(Guid userId)
    {
        if (userId == UserId)
        {
            _ = InvokeAsync(async () =>
            {
                _card = await Cards.GetAsync(UserId);
                StateHasChanged();
            });
        }
    }
}
