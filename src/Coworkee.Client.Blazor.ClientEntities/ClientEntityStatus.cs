namespace Coworkee.Client.Blazor.ClientEntities;

public enum ClientEntitySyncState
{
    Idle,
    Downloading,
    Updating,

    /// <summary>The set has more rows than the server lets clients mirror; queries keep going to the server.</summary>
    TooLarge,
    Failed,
}

/// <param name="IsLocal">Queries run on the local copy; before that, and for queries it cannot answer, they go to the server.</param>
/// <param name="Revision">Grows whenever the local rows change, e.g. to reload a view.</param>
public sealed record ClientEntityStatus(ClientEntitySyncState State, bool IsLocal, int Loaded, long Total, DateTimeOffset? Version, string? Error, int Revision)
{
    public static readonly ClientEntityStatus Initial = new(ClientEntitySyncState.Idle, false, 0, 0, null, null, 0);

    public double Progress => Total <= 0 ? 1 : Math.Min(1, (double)Loaded / Total);
}
