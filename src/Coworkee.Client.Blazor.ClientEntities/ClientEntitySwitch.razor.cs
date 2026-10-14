using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// The client entity control of a data table (added to every table by AddCoworkeeClientEntities): an icon for where searching runs with the
/// sync state as a ring around it; its menu switches between browser and server and compares both on the current query.
/// </summary>
public partial class ClientEntitySwitch : IDisposable
{
    private const int Rounds = 3;

    private ClientEntitySet? _set;
    private int _revision;
    private ClientEntityMode _mode;
    private bool _comparing;

    // without AddCoworkeeClientEntities the switch stays empty instead of failing the page
    [Inject] private IServiceProvider Services { get; set; } = null!;

    private ClientEntities? Entities => Services.GetService(typeof(ClientEntities)) as ClientEntities;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Parameter, EditorRequired] public IDataTable Table { get; set; } = null!;

    protected override void OnInitialized()
    {
        if (Entities is not { } entities)
        {
            return;
        }

        Track(entities.Find(Table.EntitySet));
        entities.Changed += OnChanged;
    }

    public void Dispose()
    {
        if (Entities is { } entities)
        {
            entities.Changed -= OnChanged;
        }
    }

    private void Track(ClientEntitySet? set)
    {
        _set = set;
        _revision = set?.Status.Revision ?? 0;
        _mode = set?.Mode ?? ClientEntityMode.Client;
    }

    private void OnChanged(ClientEntitySet set)
    {
        if (!string.Equals(set.EntitySet, Table.EntitySet, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var reload = _set is not null && (set.Status.Revision != _revision || set.Mode != _mode);
        Track(set);
        _ = InvokeAsync(async () =>
        {
            StateHasChanged();
            if (reload)
            {
                await Table.ReloadAsync();
            }
        });
    }

    private Task ChooseAsync(ClientEntityMode mode) => Entities!.SetModeAsync(Table.EntitySet, mode);

    // the same query on both sides, each round with a condition every row meets, so neither answers from a result it kept
    private async Task CompareAsync()
    {
        if (_set is not { } set)
        {
            return;
        }

        _comparing = true;
        try
        {
            var client = new List<TimeSpan>();
            var server = new List<TimeSpan>();
            (long? Client, long? Server) counts = default;
            for (var round = 0; round < Rounds; round++)
            {
                var query = Table.CurrentQuery with { Filter = Data.ODataFilter.And(Table.CurrentQuery.Filter, $"Id ne {Guid.NewGuid()}") };
                var local = await set.MeasureAsync(query, ClientEntityMode.Client);
                var remote = await set.MeasureAsync(query, ClientEntityMode.Server);
                client.Add(local.Elapsed);
                server.Add(remote.Elapsed);
                counts = (local.Count, remote.Count);
            }

            var (browser, api) = (Median(client), Median(server));
            var faster = browser <= api ? ClientEntityMode.Client : ClientEntityMode.Server;
            var same = counts.Client == counts.Server;
            Snackbar.Add(string.Join(' ',
                L["Browser {0} ms, server {1} ms.", browser, api],
                same ? L["Both found {0} rows.", counts.Client ?? 0] : L["The row counts differ: browser {0}, server {1}.", counts.Client ?? 0, counts.Server ?? 0],
                L[faster == ClientEntityMode.Client ? "Searching in the browser from now on." : "Searching on the server from now on."]),
                same ? Severity.Success : Severity.Warning);
            await ChooseAsync(faster);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Snackbar.Add(L["Comparing failed: {0}", exception.Message], Severity.Error);
        }
        finally
        {
            _comparing = false;
        }
    }

    private static long Median(List<TimeSpan> times) => (long)times.Order().ElementAt(times.Count / 2).TotalMilliseconds;

    private static bool SearchesLocally(ClientEntitySet set) => set.Mode == ClientEntityMode.Client && set.Status.IsLocal;

    private static string State(ClientEntityStatus status) => status.State switch
    {
        ClientEntitySyncState.Downloading => "downloading",
        ClientEntitySyncState.Updating => "updating",
        ClientEntitySyncState.Failed => "failed",
        ClientEntitySyncState.TooLarge => "too-large",
        _ => status.IsLocal ? "synced" : "idle",
    };

    private static Color RingColor(ClientEntityStatus status) => status switch
    {
        { State: ClientEntitySyncState.Failed } => Color.Error,
        { State: ClientEntitySyncState.TooLarge } => Color.Warning,
        { State: ClientEntitySyncState.Idle, IsLocal: true } => Color.Success,
        _ => Color.Primary,
    };

    private static double RingValue(ClientEntityStatus status) => status.State switch
    {
        ClientEntitySyncState.Downloading => status.Progress * 100,
        ClientEntitySyncState.Idle when !status.IsLocal => 0,
        _ => 100,
    };

    private string Describe(ClientEntitySet set) => set.Status switch
    {
        { State: ClientEntitySyncState.Downloading } status => L["Loading {0} of {1} rows for fast local search", status.Loaded, status.Total],
        { State: ClientEntitySyncState.TooLarge } => L["Too many rows to keep locally, searching on the server"],
        { State: ClientEntitySyncState.Failed, IsLocal: false } status => L["Searching on the server, loading failed: {0}", status.Error],
        { IsLocal: true } status when set.Mode == ClientEntityMode.Server => L["Searching on the server by choice, local copy updated {0:t}", status.Version?.ToLocalTime()],
        { State: ClientEntitySyncState.Failed } status => L["Working locally, updating failed: {0}", status.Error],
        { IsLocal: true } status => L["Searching locally, updated {0:t}", status.Version?.ToLocalTime()],
        _ => L["Searching on the server"],
    };
}
