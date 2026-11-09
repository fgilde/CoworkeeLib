using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>"My sessions" on the security tab: the user's sign-ins, live; he ends the others.</summary>
public partial class ProfileSessions : IAsyncDisposable
{
    private IReadOnlyList<UserSessionDto>? _sessions;
    private IAsyncDisposable? _subscription;

    [Inject] private ISessionsApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // a session of the user changes on his own topic: a sign-in elsewhere, one that ended
        if (ClientUser.From(AuthenticationState is null ? null : (await AuthenticationState).User).TryGetUserId(out var me))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(me), envelope =>
                EntityChangedPayload.From(envelope)?.EntityType == "UserSession" ? InvokeAsync(async () =>
                {
                    await LoadAsync();
                    StateHasChanged();
                }) : Task.CompletedTask);
        }

        await LoadAsync();
    }

    private Task<bool> LoadAsync() => Snackbar.RunAsync(async () => _sessions = await Api.GetMineAsync());

    private async Task EndAsync(UserSessionDto session)
    {
        if (await Dialogs.ConfirmAsync(L["End session"], L["Sign out {0}?", Describe(session)], L["End"], L["Cancel"], Icons.Material.Outlined.Logout)
            && await Snackbar.RunAsync(() => Api.EndAsync(session.Id), L["Session ended"]))
        {
            await LoadAsync();
        }
    }

    internal static string Describe(UserSessionDto session) => string.Join(", ", new[] { session.Device, session.Browser, session.IpAddress }.Where(p => p is { Length: > 0 }));

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }
}
