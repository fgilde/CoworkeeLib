using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>
/// The assistant's window and its button where <see cref="AiSettingNames.Display"/> puts it: floating at a bottom corner, in the app bar (the instance with
/// <see cref="InAppBar"/>), in the navigation or nowhere. There for users with Ai.Chat while the assistant is configured; Ctrl+I opens it in every mode.
/// </summary>
public partial class AssistantLauncher : IAsyncDisposable
{
    private bool _available;
    private IJSObjectReference? _hotkey;
    private DotNetObjectReference<AssistantLauncher>? _self;

    /// <summary>Only the app bar button (while the display is the app bar); the window, the floating button and Ctrl+I stay with the layout's launcher.</summary>
    [Parameter] public bool InAppBar { get; set; }

    /// <summary>With <see cref="InAppBar"/>: a menu item for the app bar's overflow menu.</summary>
    [Parameter] public bool Nested { get; set; }

    private string Display => Themes.ClientSettings.GetValueOrDefault(AiSettingNames.Display) is { } display && AssistantDisplay.All.Contains(display)
        ? display
        : AssistantDisplay.BottomRight;

    private bool Floating => Display is AssistantDisplay.BottomRight or AssistantDisplay.BottomLeft;

    private string Corner => Display switch
    {
        AssistantDisplay.BottomRight => "right",
        AssistantDisplay.BottomLeft => "left",
        _ => "docked",
    };

    [Inject] private IAssistantApi Api { get; set; } = null!;

    [Inject] private AssistantState State { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private Theming.ThemeService Themes { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        State.Changed += Refresh;
        // switched on or off by an administrator, or Ai.Chat granted or taken: the button follows at once (SettingsLiveUpdates, PermissionStore)
        Themes.Changed += Recheck;
        Permissions.Changed += Recheck;
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        try
        {
            _available = await Permissions.HasAsync(AiPermissions.Chat) && (await Api.GetStatusAsync()).Available;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            _available = false;
        }
    }

    private void Recheck() => InvokeAsync(async () =>
    {
        var was = _available;
        await CheckAsync();
        if (was && !_available && !InAppBar)
        {
            State.Close();
        }

        // the display may have changed with the settings
        StateHasChanged();
    });

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_available && !InAppBar && _self is null)
        {
            _self = DotNetObjectReference.Create(this);
            var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            _hotkey = await module.InvokeAsync<IJSObjectReference>("onHotkey", _self, "i");
        }
    }

    [JSInvokable]
    public Task ToggleFromHotkey() => InvokeAsync(State.Toggle);

    public async ValueTask DisposeAsync()
    {
        State.Changed -= Refresh;
        Themes.Changed -= Recheck;
        Permissions.Changed -= Recheck;
        try
        {
            if (_hotkey is not null)
            {
                await _hotkey.InvokeVoidAsync("dispose");
                await _hotkey.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
    }

    private void Refresh() => InvokeAsync(StateHasChanged);
}
