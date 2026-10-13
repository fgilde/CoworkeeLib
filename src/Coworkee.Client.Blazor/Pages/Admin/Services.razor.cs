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
    private IReadOnlyList<string> _unavailable = [];
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
            var result = await Api.GetServicesAsync(_stop.Token);
            _services = result.Services;
            _unavailable = result.UnavailableSources;
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

    private string SourceName(string source) => source switch
    {
        "app" => L["App"],
        "aspireui" => "AspireUI",
        _ => source,
    };

    private static IEnumerable<string> Urls(ServiceDto service) =>
        (service.Urls is { Count: > 0 } ? service.Urls : [service.Url]).Where(u => u.Length > 0);

    private async Task Open(ServiceDto service)
    {
        if (Urls(service).FirstOrDefault() is { } url)
        {
            await Js.InvokeVoidAsync("open", url, "_blank", "noopener");
        }
    }

    private static Color HealthColor(ServiceHealth health) => health switch
    {
        ServiceHealth.Healthy => Color.Success,
        ServiceHealth.Unhealthy => Color.Warning,
        ServiceHealth.Unknown => Color.Default,
        _ => Color.Error,
    };

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
