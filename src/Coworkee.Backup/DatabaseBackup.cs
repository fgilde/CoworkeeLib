using Coworkee.Domain;

namespace Coworkee.Backup;

public sealed class DatabaseBackup : AuditedEntity
{
    public required string Name { get; set; }

    public required string BlobKey { get; set; }

    public long Size { get; set; }

    public int Tables { get; set; }

    public string? Migration { get; set; }
}
