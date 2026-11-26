using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    private static readonly TimeSpan LiveSettle = TimeSpan.FromMilliseconds(250);
    private CancellationTokenSource? _live;

    // the settle delay runs on the registered clock, so tests can advance it instead of sleeping
    private TimeProvider Clock => Services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

    /// <summary>
    /// Reloads when rows are created, changed or deleted in any session. Defaults to "type:" and the row type without "Dto"
    /// (BrandDto follows the [Realtime] entity Brand); an empty string turns it off. Tables over a <see cref="DataSource"/> stay quiet.
    /// </summary>
    [Parameter] public string? RealtimeTopic { get; set; }

    private string LiveTopic => RealtimeTopic ?? (DataSource is null ? RealtimeTopics.Type(EntityName) : string.Empty);

    // the data filters that apply to the user changed: the rows a server read returns change with them (quiet where the table is)
    private string FilterTopic => LiveTopic.Length == 0 ? string.Empty : DataFilterTopics.TenantChanged;

    private string SystemFilterTopic => LiveTopic.Length == 0 ? string.Empty : DataFilterTopics.Changed;

    private static string EntityName => typeof(T).Name.EndsWith("Dto", StringComparison.Ordinal) ? typeof(T).Name[..^3] : typeof(T).Name;

    // without a receiver: an event does not render the table, a burst of them (one per changed row) would render it twice per event
    private EventCallback<RealtimeEnvelope> _liveEvent;

    private EventCallback<RealtimeEnvelope> LiveEvent =>
        _liveEvent.HasDelegate ? _liveEvent : _liveEvent = new EventCallback<RealtimeEnvelope>(null, (Func<RealtimeEnvelope, Task>)(_ => LiveChangedAsync()));

    // a bulk delete sends one event per row; one reload for the burst
    private async Task LiveChangedAsync()
    {
        _live?.Cancel();
        var live = _live = new CancellationTokenSource();
        try
        {
            await Task.Delay(LiveSettle, Clock, live.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ReloadAsync();
        StateHasChanged();
    }
}
