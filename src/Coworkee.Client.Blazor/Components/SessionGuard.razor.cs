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
    private IAsyncDisposable? _session;
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
        var sid = user?.FindFirst("sid")?.Value;
        if (ClientUser.From(user).TryGetUserId(out var userId))
        {
            // a new password of this very session ends the others, not this one
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(userId), envelope =>
                envelope.Type == RealtimeEventTypes.SessionRevoked && !IsKept(envelope, sid) ? InvokeAsync(() => EndAsync(envelope)) : Task.CompletedTask);
        }

        // the impersonation of this session ended elsewhere (another tab, another app, its expiry): the sign-in goes back to the auth server, which knows who is signed in now
        if (sid is { Length: > 0 } && user!.HasClaim(c => c.Type == ImpersonationClaims.Impersonator))
        {
            _session = await Realtime.SubscribeAsync(RealtimeTopics.Session(sid),
                envelope => envelope.Type == RealtimeEventTypes.ImpersonationEnded ? InvokeAsync(SignInAgain) : Task.CompletedTask);
        }
    }

    private static bool IsKept(RealtimeEnvelope envelope, string? sid) =>
        sid is { Length: > 0 } && envelope.Payload.Deserialize<SessionRevokedPayload>(JsonSerializerOptions.Web)?.KeptSessionId == sid;

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

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }
}
