using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>The assistant button at the bottom right and its window; there for users with Ai.Chat while the assistant is configured. Ctrl+I opens it.</summary>
public partial class AssistantLauncher : IAsyncDisposable
{
    private bool _available;
    private IJSObjectReference? _hotkey;
    private DotNetObjectReference<AssistantLauncher>? _self;

    [Inject] private IAssistantApi Api { get; set; } = null!;

    [Inject] private AssistantState State { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        State.Changed += Refresh;
        try
        {
            _available = await Permissions.HasAsync(AiPermissions.Chat) && (await Api.GetStatusAsync()).Available;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            _available = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_available && _self is null)
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
