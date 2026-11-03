namespace Coworkee.Cli.Apps;

internal sealed record VersionRules(string Version, string[]? Breaking, bool NeedsMigration, RenameRule[]? Renames, InsertRule[]? Insertions, PackageRule[]? Packages, ManualRule[]? Manual);
