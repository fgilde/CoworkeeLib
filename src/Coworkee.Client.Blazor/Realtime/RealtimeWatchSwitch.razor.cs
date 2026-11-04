using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Realtime;

/// <summary>
/// Switches the server's watch of a set another system writes (AddRealtimeWatch) on and off, e.g. above a table of it to see what live updates cost.
/// Shown only to who may manage watches (RealtimePermissions.ManageWatches) and only for a watched set.
/// </summary>
public partial class RealtimeWatchSwitch
{
    private RealtimeWatchDto? _watch;
    private bool _saving;

    // without the api (a host or test that registers none) the switch stays empty instead of failing the page
    [Inject] private IServiceProvider Services { get; set; } = null!;

    private IRealtimeWatchApi? Watches => Services.GetService(typeof(IRealtimeWatchApi)) as IRealtimeWatchApi;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    /// <summary>The watched entity type, as the topics name it (the CLR type's name, e.g. "CalculatedShipment").</summary>
    [Parameter, EditorRequired] public string EntityType { get; set; } = null!;

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            _watch = Watches is not { } watches ? null : (await watches.GetAsync()).FirstOrDefault(w => string.Equals(w.EntityType, EntityType, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException)
        {
            // an app without watches (or an older server) shows no switch
            _watch = null;
        }
    }

    private async Task SetAsync(bool enabled)
    {
        if (_watch is not { } watch || Watches is not { } watches)
        {
            return;
        }

        _saving = true;
        try
        {
            await watches.SetAsync(watch.EntityType, enabled);
            _watch = watch with { Enabled = enabled };
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }
}
