using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Storage;

/// <summary>Blob storage on the file system, S3 (MinIO) or Azure Blob, chosen by <c>Coworkee:Storage:Provider</c>.</summary>
public sealed class CoworkeeStorageModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var options = context.Configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
        services.TryAddSingleton<IBlobStorage>(options.Provider switch
        {
            StorageProviders.FileSystem => new FileSystemBlobStorage(options.FileSystem),
            StorageProviders.S3 => new S3BlobStorage(options.S3),
            StorageProviders.AzureBlob => new AzureBlobStorage(options.AzureBlob),
            _ => throw new InvalidOperationException($"Unknown storage provider '{options.Provider}'. Use {StorageProviders.FileSystem}, {StorageProviders.S3} or {StorageProviders.AzureBlob}."),
        });
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<BlobLinks>();
    }
}
