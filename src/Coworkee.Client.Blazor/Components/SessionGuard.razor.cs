using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
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

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (Guid.TryParse(user?.FindFirst("sub")?.Value, out var userId))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(userId),
                envelope => envelope.Type == RealtimeEventTypes.SessionRevoked ? InvokeAsync(() => EndAsync(envelope)) : Task.CompletedTask);
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

        var locked = envelope.Payload.Deserialize<SessionRevokedPayload>(JsonSerializerOptions.Web)?.Reason == "locked";
        await Dialogs.ShowMessageBoxAsync(L["Signed out"], locked ? L["An administrator locked your account."] : L["An administrator ended your session."]);
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
