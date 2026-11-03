namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Keeps the local copies between visits. The default writes to IndexedDB; replace it to keep them elsewhere or register <see cref="NoClientEntityStorage"/> to keep nothing.</summary>
public interface IClientEntityStorage
{
    Task<byte[]?> LoadAsync(string key);

    Task SaveAsync(string key, byte[] data);

    Task ClearAsync();

    /// <summary>
    /// Removes the entries whose keys start with <paramref name="prefix"/>: the pages of a finished download and the copies of group selections
    /// used long ago. The default removes nothing; such entries then stay until <see cref="ClearAsync"/> (sign-out, another user).
    /// </summary>
    Task DeleteAsync(string prefix) => Task.CompletedTask;
}
