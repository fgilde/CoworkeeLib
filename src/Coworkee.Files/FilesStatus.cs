using Coworkee.Application.Status;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Coworkee.Files;

/// <summary>Files of every organisation, deleted ones included: they keep their blobs until they are purged.</summary>
internal sealed class FilesStatus(CoworkeeDbContext db, IConfiguration configuration) : ISystemStatusContributor
{
    public async Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken)
    {
        var files = db.Set<StoredFile>().IgnoreQueryFilters();
        var count = await files.CountAsync(cancellationToken);
        var bytes = await files.SumAsync(f => (long?)f.Size, cancellationToken) ?? 0;
        return new SystemStatusSection("files", "Files",
        [
            new("Storage", configuration.GetSection(StorageOptions.Section).Get<StorageOptions>()?.Provider ?? StorageProviders.FileSystem),
            new("Files", count.ToString()),
            new("Storage used", SystemStatusExtensions.Bytes(bytes)),
        ], Order: 6);
    }
}
