using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Amazon.S3.Util;
using Coworkee.Contracts.Configuration;

namespace Coworkee.Storage;

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
        try
        {
            await _client.DeleteObjectAsync(options.Bucket, key, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode == "NoSuchBucket")
        {
            // nothing was ever stored
        }
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
