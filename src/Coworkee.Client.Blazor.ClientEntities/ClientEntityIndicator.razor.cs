using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Shows whether a set is queried locally, still downloading or on the server, and tells the page when the local rows changed.</summary>
public partial class ClientEntityIndicator : IDisposable
{
    private ClientEntitySet? _set;
    private int _revision;

    // without AddCoworkeeClientEntities the indicator stays empty instead of failing the page
    [Inject] private IServiceProvider Services { get; set; } = null!;

    private ClientEntities? Entities => Services.GetService(typeof(ClientEntities)) as ClientEntities;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    /// <summary>The local rows changed, e.g. reload a table: <c>DataChanged="_table.ReloadAsync"</c>.</summary>
    [Parameter] public EventCallback DataChanged { get; set; }

    protected override void OnInitialized()
    {
        if (Entities is not { } entities)
        {
            return;
        }

        _set = entities.Find(EntitySet);
        _revision = _set?.Status.Revision ?? 0;
        entities.Changed += OnChanged;
    }

    public void Dispose()
    {
        if (Entities is { } entities)
        {
            entities.Changed -= OnChanged;
        }
    }

    private void OnChanged(ClientEntitySet set)
    {
        if (!string.Equals(set.EntitySet, EntitySet, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _set = set;
        var changed = set.Status.Revision != _revision;
        _revision = set.Status.Revision;
        _ = InvokeAsync(async () =>
        {
            StateHasChanged();
            if (changed)
            {
                await DataChanged.InvokeAsync();
            }
        });
    }

    private static string Icon(ClientEntityStatus status) => status switch
    {
        { State: ClientEntitySyncState.Failed } => Icons.Material.Outlined.CloudOff,
        { State: ClientEntitySyncState.Updating } => Icons.Material.Outlined.CloudSync,
        { IsLocal: true } => Icons.Material.Outlined.OfflinePin,
        _ => Icons.Material.Outlined.Cloud,
    };

    private string Describe(ClientEntityStatus status) => status switch
    {
        { State: ClientEntitySyncState.Downloading } => L["Loading {0} of {1} rows for fast local search", status.Loaded, status.Total],
        { State: ClientEntitySyncState.TooLarge } => L["Too many rows to keep locally, searching on the server"],
        { State: ClientEntitySyncState.Failed, IsLocal: true } => L["Working locally, updating failed: {0}", status.Error],
        { State: ClientEntitySyncState.Failed } => L["Searching on the server, loading failed: {0}", status.Error],
        { IsLocal: true } => L["Searching locally, updated {0:t}", status.Version?.ToLocalTime()],
        _ => L["Searching on the server"],
    };
}
