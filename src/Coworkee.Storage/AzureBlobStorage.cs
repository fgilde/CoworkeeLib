using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Coworkee.Contracts.Configuration;

namespace Coworkee.Storage;

internal sealed class AzureBlobStorage(AzureBlobStorageOptions options) : IBlobStorage
{
    private readonly BlobContainerClient _container = new(options.ConnectionString, options.Container);
    private volatile bool _containerReady;

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        if (!_containerReady)
        {
            await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerReady = true;
        }

        await _container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        try
        {
            return await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var start = BlobKeys.Validate(prefix) + "/";
        try
        {
            await foreach (var blob in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = start }, cancellationToken))
            {
                await _container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        try
        {
            await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        try
        {
            return (await _container.GetBlobClient(key).ExistsAsync(cancellationToken)).Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
    }
}
