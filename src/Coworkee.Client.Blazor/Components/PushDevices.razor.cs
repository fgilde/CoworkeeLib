using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Layout;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The user's switch for push notifications on this browser and the list of his devices that receive them; hidden while push is not configured.</summary>
public partial class PushDevices
{
    private PushSettingsDto? _settings;
    private PushState _state = new("unsupported", null);
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private PwaService Pwa { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    private bool Possible => _state.Permission is "default" or "granted";

    private bool On => _state.Endpoint is { } endpoint && _settings?.Devices.Any(d => d.Endpoint == endpoint) == true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _settings = await Api.GetPushSettingsAsync();
        }
        catch (ApiException)
        {
            return;
        }

        if (_settings?.PublicKey is not null)
        {
            await Pwa.StartAsync();
            await ReadStateAsync();
        }
    }

    private async Task ReadStateAsync()
    {
        try
        {
            _state = Pwa.Active ? await Pwa.PushStateAsync() : new PushState("unsupported", null);
        }
        catch (JSException)
        {
            _state = new PushState("unsupported", null);
        }
    }

    private async Task ToggleAsync(bool on)
    {
        _busy = true;
        try
        {
            if (on && await Pwa.SubscribeAsync(_settings!.PublicKey!) is { } subscription)
            {
                await Api.SavePushSubscriptionAsync(subscription);
            }
            else if (!on && await Pwa.UnsubscribeAsync() is { } endpoint && _settings!.Devices.FirstOrDefault(d => d.Endpoint == endpoint) is { } device)
            {
                await Api.DeletePushSubscriptionAsync(device.Id);
            }
        }
        catch (Exception exception) when (exception is ApiException or JSException)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }

        await ReloadAsync();
    }

    private async Task RemoveAsync(PushDeviceDto device)
    {
        _busy = true;
        try
        {
            if (device.Endpoint == _state.Endpoint)
            {
                await Pwa.UnsubscribeAsync();
            }

            await Api.DeletePushSubscriptionAsync(device.Id);
        }
        catch (Exception exception) when (exception is ApiException or JSException)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            _settings = await Api.GetPushSettingsAsync();
        }
        catch (ApiException)
        {
        }

        await ReadStateAsync();
        _busy = false;
    }
}
