namespace Coworkee.Contracts.Configuration;

public sealed class S3StorageOptions
{
    /// <summary>Endpoint for MinIO or other S3 compatible stores; empty uses AWS with <see cref="Region"/>.</summary>
    public string? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    public string Bucket { get; set; } = "coworkee";

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Creates a missing bucket on first write; turn off where the credentials may not create buckets.</summary>
    public bool CreateBucket { get; set; } = true;
}
