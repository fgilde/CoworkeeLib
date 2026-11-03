using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Reloads the client settings when anyone saves a setting that reaches this user (global, the organisation, the user's own): everything that follows
/// <see cref="ThemeService.Changed"/> (the assistant, the page mode of tables, the theme) changes at once, without a reload. CoworkeeLayout renders it.
/// </summary>
public partial class SettingsLiveUpdates
{
    private string _userTopic = string.Empty;
    private Task? _reload;
    private bool _again;

    [Inject] private ThemeService Themes { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        _userTopic = Guid.TryParse(user?.FindFirst("sub")?.Value, out var id) ? RealtimeTopics.User(id) : string.Empty;
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
