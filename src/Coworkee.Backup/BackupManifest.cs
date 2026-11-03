namespace Coworkee.Backup;

internal sealed record BackupManifest(string? Migration, IReadOnlyList<string> Tables);
