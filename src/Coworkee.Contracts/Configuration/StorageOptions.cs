namespace Coworkee.Contracts.Configuration;

public sealed class StorageOptions
{
    public const string Section = "Coworkee:Storage";

    public string Provider { get; set; } = StorageProviders.FileSystem;

    public FileSystemStorageOptions FileSystem { get; set; } = new();

    public S3StorageOptions S3 { get; set; } = new();

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();
}
