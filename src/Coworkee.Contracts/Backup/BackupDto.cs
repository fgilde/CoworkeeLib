namespace Coworkee.Contracts.Backup;

public sealed record BackupDto(Guid Id, string Name, long Size, int Tables, string? Migration, DateTimeOffset CreatedAt);
