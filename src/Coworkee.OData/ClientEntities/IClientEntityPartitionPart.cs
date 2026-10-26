namespace Coworkee.OData.ClientEntities;

/// <summary>Adds to the partition of the client-entity mirror: when a part changes, the browser drops its rows and syncs again.</summary>
public interface IClientEntityPartitionPart
{
    /// <summary>The part of the current request, or null when it adds nothing.</summary>
    Task<string?> GetAsync(CancellationToken ct);
}
