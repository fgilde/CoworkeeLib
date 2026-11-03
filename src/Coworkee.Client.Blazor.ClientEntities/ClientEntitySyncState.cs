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
