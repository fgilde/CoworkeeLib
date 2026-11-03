using Coworkee.Contracts.Configuration;

namespace Coworkee.Storage;

internal sealed class FileSystemBlobStorage(FileSystemStorageOptions options) : IBlobStorage
{
    private readonly string _root = Path.GetFullPath(options.Root);

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true) : null);
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var path = PathOf(prefix);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        // a key whose directory is gone is no error either, as on the other stores
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) => Task.FromResult(File.Exists(PathOf(key)));

    private string PathOf(string key) => Path.Combine(_root, BlobKeys.Validate(key).Replace('/', Path.DirectorySeparatorChar));
}
