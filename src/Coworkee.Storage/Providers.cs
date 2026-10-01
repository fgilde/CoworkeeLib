using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Coworkee.Storage;

public sealed class StorageOptions
{
    public const string Section = "Coworkee:Storage";

    public string Provider { get; set; } = StorageProviders.FileSystem;

    public FileSystemStorageOptions FileSystem { get; set; } = new();

    public S3StorageOptions S3 { get; set; } = new();

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();
}

public sealed class FileSystemStorageOptions
{
    public string Root { get; set; } = "App_Data/blobs";
}

public sealed class S3StorageOptions
{
    /// <summary>Endpoint for MinIO or other S3 compatible stores; empty uses AWS with <see cref="Region"/>.</summary>
    public string? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    public string Bucket { get; set; } = "coworkee";

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }
}

public sealed class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";

    public string Container { get; set; } = "coworkee";
}

internal sealed class FileSystemBlobStorage(FileSystemStorageOptions options) : IBlobStorage
{
    private readonly string _root = Path.GetFullPath(options.Root);

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        File.Move(temp, path, overwrite: true);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) => Task.FromResult(File.Exists(PathOf(key)));

    private string PathOf(string key) => Path.Combine(_root, BlobKeys.Validate(key).Replace('/', Path.DirectorySeparatorChar));
}

internal sealed class S3BlobStorage(S3StorageOptions options) : IBlobStorage, IDisposable
{
    private readonly AmazonS3Client _client = Create(options);
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        await EnsureBucketAsync(cancellationToken);
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = options.Bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false }, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        try
        {
            var response = await _client.GetObjectAsync(options.Bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        await _client.DeleteObjectAsync(options.Bucket, key, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        try
        {
            await _client.GetObjectMetadataAsync(options.Bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _bucketLock.Dispose();
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketReady)
        {
            return;
        }

        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (!_bucketReady)
            {
                try
                {
                    await _client.PutBucketAsync(options.Bucket, cancellationToken);
                }
                catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
                {
                }

                _bucketReady = true;
            }
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    private static AmazonS3Client Create(S3StorageOptions options)
    {
        var config = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region) }
            : new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = options.Region };
        return options.AccessKey is { Length: > 0 } accessKey
            ? new AmazonS3Client(new BasicAWSCredentials(accessKey, options.SecretKey), config)
            : new AmazonS3Client(config);
    }
}

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
