using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Services : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stop = new();
    private IReadOnlyList<ServiceDto>? _services;
    private string? _error;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IJSRuntime Js { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                await InvokeAsync(RefreshAsync);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync()
    {
        _busy = true;
        try
        {
            _services = await Api.GetServicesAsync(_stop.Token);
            _error = null;
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 403 ? L["The services are shown in the system organisation."] : exception.Message;
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }

    private async Task Open(ServiceDto service) => await Js.InvokeVoidAsync("open", service.Url, "_blank", "noopener");

    private static Color HealthColor(ServiceHealth health) => health switch
    {
        ServiceHealth.Healthy => Color.Success,
        ServiceHealth.Unhealthy => Color.Warning,
        _ => Color.Error,
    };

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
