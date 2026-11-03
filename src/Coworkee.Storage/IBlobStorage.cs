namespace Coworkee.Storage;

/// <summary>Stores opaque blobs by key; keys come from <see cref="BlobKeys"/> and are validated by every provider.</summary>
public interface IBlobStorage
{
    Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken);

    /// <summary>Opens the blob as a seekable stream with a known length, or returns null when it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Removes every blob whose key starts with <paramref name="prefix"/> followed by '/'.</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>Removes the blob; missing blobs are ignored.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}
