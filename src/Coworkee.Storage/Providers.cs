using Amazon.Runtime;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Amazon.S3.Util;
using Amazon.S3;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Azure;
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

        // multipart above the threshold, so objects beyond the 5 GB single put limit work
        using var transfer = new TransferUtility(_client);
        await transfer.UploadAsync(
            new TransferUtilityUploadRequest { BucketName = options.Bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        BlobKeys.Validate(key);
        try
        {
            var metadata = await _client.GetObjectMetadataAsync(options.Bucket, key, cancellationToken);
            return new S3ReadStream(_client, options.Bucket, key, metadata.ContentLength);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var request = new ListObjectsV2Request { BucketName = options.Bucket, Prefix = BlobKeys.Validate(prefix) + "/" };
        ListObjectsV2Response response;
        do
        {
            try
            {
                response = await _client.ListObjectsV2Async(request, cancellationToken);
            }
            catch (AmazonS3Exception exception) when (exception.ErrorCode == "NoSuchBucket")
            {
                return;
            }

            if (response.S3Objects is { Count: > 0 } objects)
            {
                await _client.DeleteObjectsAsync(
                    new DeleteObjectsRequest { BucketName = options.Bucket, Objects = objects.Select(o => new KeyVersion { Key = o.Key }).ToList() },
                    cancellationToken);
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);
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
        if (_bucketReady || !options.CreateBucket)
        {
            return;
        }

        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (!_bucketReady && !await AmazonS3Util.DoesS3BucketExistV2Async(_client, options.Bucket))
            {
                try
                {
                    await _client.PutBucketAsync(options.Bucket, cancellationToken);
                }
                catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
                {
                }
            }

            _bucketReady = true;
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

        // ranged reads cannot be checked against the whole object's checksum
        config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        return options.AccessKey is { Length: > 0 } accessKey
            ? new AmazonS3Client(new BasicAWSCredentials(accessKey, options.SecretKey), config)
            : new AmazonS3Client(config);
    }
}

/// <summary>Seekable read over an S3 object: each read after a seek opens a ranged GET from the current position.</summary>
internal sealed class S3ReadStream(IAmazonS3 client, string bucket, string key, long length) : Stream
{
    private Stream? _body;
    private GetObjectResponse? _response;
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= length || buffer.Length == 0)
        {
            return 0;
        }

        if (_body is null)
        {
            _response = await client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key, ByteRange = new ByteRange(_position, length - 1) }, cancellationToken);
            _body = _response.ResponseStream;
        }

        var read = await _body.ReadAsync(buffer, cancellationToken);
        _position += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };
        ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(offset));
        if (target != _position)
        {
            CloseBody();
            _position = target;
        }

        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseBody();
        }

        base.Dispose(disposing);
    }

    private void CloseBody()
    {
        _body?.Dispose();
        _response?.Dispose();
        _body = null;
        _response = null;
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
