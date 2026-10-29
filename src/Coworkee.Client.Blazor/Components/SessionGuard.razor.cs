using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class SessionGuard : IAsyncDisposable
{
    private IAsyncDisposable? _subscription;
    private bool _ended;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        var impersonator = user?.FindFirst(ImpersonationClaims.Impersonator)?.Value;
        if (ClientUser.From(user).TryGetUserId(out var userId))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(userId), envelope => envelope.Type switch
            {
                RealtimeEventTypes.SessionRevoked => InvokeAsync(() => EndAsync(envelope)),
                RealtimeEventTypes.ImpersonationEnded when Ended(envelope, impersonator) => InvokeAsync(SignInAgain),
                _ => Task.CompletedTask,
            });
        }
    }

    // the impersonation of this session ended elsewhere (another tab, another app, its expiry): the sign-in goes back to the auth server, which knows who is signed in now
    private static bool Ended(RealtimeEnvelope envelope, string? impersonator) =>
        impersonator is not null && envelope.Payload.Deserialize<ImpersonationEndedPayload>(JsonSerializerOptions.Web)?.ImpersonatorId.ToString() == impersonator;

    private void SignInAgain()
    {
        if (!_ended)
        {
            _ended = true;
            Nav.NavigateTo(Options.SignInHref(Nav.ToBaseRelativePath(Nav.Uri)), forceLoad: true);
        }
    }

    private async Task EndAsync(RealtimeEnvelope envelope)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        var redirect = "/";
        try
        {
            redirect = (await Api.LogoutAsync()).Redirect;
        }
        catch (ApiException)
        {
        }

        var message = envelope.Payload.Deserialize<SessionRevokedPayload>(JsonSerializerOptions.Web)?.Reason switch
        {
            "locked" => L["An administrator locked your account."],
            "password-changed" => L["The password of your account was changed. Please sign in again."],
            _ => L["An administrator ended your session."],
        };
        await Dialogs.ShowMessageBoxAsync(L["Signed out"], message);
        Nav.NavigateTo(redirect, forceLoad: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }
}
