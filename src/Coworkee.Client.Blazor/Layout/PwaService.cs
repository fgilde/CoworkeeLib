using Coworkee.Contracts.Notifications;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Layout;

/// <summary>
/// The installable app (service worker, update and install prompts) and the browser side of Web Push. Everything stays off on pages without a web manifest.
/// </summary>
public sealed class PwaService(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<PwaService>? _self;

    /// <summary>A service worker runs: the app works as installed app.</summary>
    public bool Active { get; private set; }

    /// <summary>A new version is installed and waits for a reload.</summary>
    public bool UpdateReady { get; private set; }

    /// <summary>The browser offers to install the app.</summary>
    public bool CanInstall { get; private set; }

    public event Action? Changed;

    public async Task StartAsync()
    {
        if (_self is not null)
        {
            return;
        }

        _self = DotNetObjectReference.Create(this);
        try
        {
            Active = await (await ModuleAsync()).InvokeAsync<bool>("start", _self);
        }
        catch (JSException)
        {
            // no service worker (private window, unsafe origin): the app runs as plain web page
        }
    }

    [JSInvokable]
    public void OnUpdateReady()
    {
        UpdateReady = true;
        Changed?.Invoke();
    }

    [JSInvokable]
    public void OnInstallAvailable()
    {
        CanInstall = true;
        Changed?.Invoke();
    }

    public async Task ApplyUpdateAsync() => await (await ModuleAsync()).InvokeVoidAsync("applyUpdate");

    public async Task<bool> InstallAsync()
    {
        var accepted = await (await ModuleAsync()).InvokeAsync<bool>("install");
        CanInstall = false;
        Changed?.Invoke();
        return accepted;
    }

    public async Task<PushState> PushStateAsync() => await (await ModuleAsync()).InvokeAsync<PushState>("pushState");

    /// <summary>Asks for permission and subscribes this browser; null when the user refuses.</summary>
    public async Task<PushSubscriptionRequest?> SubscribeAsync(string publicKey) =>
        await (await ModuleAsync()).InvokeAsync<PushSubscriptionRequest?>("subscribe", publicKey);

    /// <summary>Ends this browser's subscription; returns its endpoint.</summary>
    public async Task<string?> UnsubscribeAsync() => await (await ModuleAsync()).InvokeAsync<string?>("unsubscribe");

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/pwa.js");

    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        try
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}

/// <summary><see cref="Permission"/>: unsupported, denied, default or granted; <see cref="Endpoint"/>: this browser's subscription.</summary>
public sealed record PushState(string Permission, string? Endpoint);
