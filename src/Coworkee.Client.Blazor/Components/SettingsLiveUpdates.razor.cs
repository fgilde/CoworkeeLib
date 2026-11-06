using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Reloads the client settings when anyone saves a setting that reaches this user (global, the organisation, the user's own): everything that follows
/// <see cref="ThemeService.Changed"/> (the assistant, the page mode of tables, the theme, the user's language) changes at once, without a reload; so do
/// languages and translations an administrator changes. CoworkeeLayout renders it.
/// </summary>
public partial class SettingsLiveUpdates
{
    private string _userTopic = string.Empty;
    private Task? _reload;
    private bool _again;

    [Inject] private ThemeService Themes { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private MudBlazor.ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        _userTopic = Guid.TryParse(user?.FindFirst("sub")?.Value, out var id) ? RealtimeTopics.User(id) : string.Empty;
    }

    // languages switched on or off, a new default, edited translations: the texts show at once, wherever the language menu is
    private async Task LanguagesChangedAsync(RealtimeEnvelope _)
    {
        var previous = L.Languages.FirstOrDefault(l => l.Culture == L.Culture)?.Name ?? L.Culture;
        if (await L.RefreshAsync() is { } fallback)
        {
            var name = L.Languages.FirstOrDefault(l => l.Culture == fallback)?.Name ?? fallback;
            Snackbar.Add(L["The language {0} was switched off; the app now shows {1}.", previous, name], MudBlazor.Severity.Info);
        }
    }

    // the user's topic carries other events too (notifications, sessions)
    private Task UserChangedAsync(RealtimeEnvelope envelope) =>
        EntityChangedPayload.From(envelope) is { EntityType: "SettingValue" } ? ReloadAsync() : Task.CompletedTask;

    // saving a form sends one event per setting: one reload for the burst, and one more for events during it
    private Task ReloadAsync()
    {
        if (_reload is { IsCompleted: false })
        {
            _again = true;
            return Task.CompletedTask;
        }

        return _reload = ReloadUntilQuietAsync();
    }

    private async Task ReloadUntilQuietAsync()
    {
        do
        {
            _again = false;
            await Themes.LoadAsync();
        }
        while (_again);
    }
}
