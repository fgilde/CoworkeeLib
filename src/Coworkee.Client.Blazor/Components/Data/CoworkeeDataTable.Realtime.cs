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

    private static string EntityName => typeof(T).Name.EndsWith("Dto", StringComparison.Ordinal) ? typeof(T).Name[..^3] : typeof(T).Name;

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
    }
}
