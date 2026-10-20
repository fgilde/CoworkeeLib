using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.People;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>A person of the organisation (where chat mentions lead): picture, name, whether online, a chat with him and, for admins, his account.</summary>
public partial class Person
{
    private UserCardDto? _card;
    private bool _online;
    private bool _mayChat;
    private bool _mayManage;
    private bool _isMe;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Inject] private Presence Presence { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private ChatDock Dock { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter] public Guid Id { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        _isMe = ClientUser.From(user).UserId == Id;
        _card = await Cards.GetAsync(Id);
        _online = await Presence.IsOnlineAsync(Id);
        _mayChat = await Permissions.HasAsync(ChatPermissions.Use);
        _mayManage = await Permissions.HasAsync(IdentityPermissions.Users.View);
    }

    private Task ChatAsync() => Dock.OpenWithAsync(Id);
}
