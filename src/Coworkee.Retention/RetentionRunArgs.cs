namespace Coworkee.Retention;

public sealed record RetentionRunArgs(string Key, int Days, Guid Token);
