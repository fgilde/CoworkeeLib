using Coworkee.Client.Blazor.Data;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>The local copy of one OData set, whatever its row type.</summary>
public abstract class ClientEntitySet : IAsyncDisposable
{
    public abstract string EntitySet { get; }

    public ClientEntityStatus Status { get; private set; } = ClientEntityStatus.Initial;

    /// <summary>The status, the mode or the rows changed.</summary>
    public event Action<ClientEntitySet>? Changed;

    /// <summary>Where OData queries run once the copy is complete; <see cref="ClientEntities.SetModeAsync"/> also remembers it for the user.</summary>
    public ClientEntityMode Mode
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                ModeChanged();
                Changed?.Invoke(this);
            }
        }
    }

    private protected virtual void ModeChanged()
    {
    }

    /// <summary>Asks the server for changes now instead of at the next interval.</summary>
    public abstract Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The copy asks the server nothing (no download, no changes) and answers from what it has; a download stops after its round. Until
    /// <see cref="ResumeSync"/> or the page is loaded again.
    /// </summary>
    public bool SyncPaused { get; private set; }

    public void PauseSync()
    {
        if (!SyncPaused)
        {
            SyncPaused = true;
            Changed?.Invoke(this);
        }
    }

    /// <summary>Syncs again: catches up at once (a download goes on where it stopped), then at the intervals and on changes.</summary>
    public void ResumeSync()
    {
        if (SyncPaused)
        {
            SyncPaused = false;
            Changed?.Invoke(this);
            _ = RefreshAsync();
        }
    }

    /// <summary>Runs <paramref name="query"/> on one side, whatever the mode, and tells how long it took and how many rows matched; the local side needs the complete copy.</summary>
    public abstract Task<(TimeSpan Elapsed, long? Count)> MeasureAsync(ODataQuery query, ClientEntityMode side, CancellationToken cancellationToken = default);

    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// The values of <paramref name="paths"/> (OData paths) of the local rows matching <paramref name="filter"/>, as text, without asking the server: null
    /// while the copy is not local or switched to the server, for a filter it cannot answer and for a path through a navigation the copy does not carry.
    /// </summary>
    internal abstract Task<IReadOnlyList<string?[]>?> LocalValuesAsync(string filter, string? orderBy, int top, IReadOnlyList<string> paths, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="query"/> answered from the local rows as the server's query_data would, with the fields of its select (without one every field the
    /// server returns) by name; null while the copy is not local or switched to the server, for a filter it cannot answer and when the row type lacks a field
    /// the server would return.
    /// </summary>
    internal abstract Task<LocalRows?> LocalRowsAsync(ODataQuery query, CancellationToken cancellationToken);

    /// <summary>Something changed on the server: refresh soon, several nudges in a row refresh once.</summary>
    internal abstract void Nudge();

    protected void Publish(ClientEntityStatus status)
    {
        Status = status;
        Changed?.Invoke(this);
    }
}
