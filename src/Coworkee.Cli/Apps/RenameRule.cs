namespace Coworkee.Cli.Apps;

/// <summary>A text or regex replacement in the files of the globs (relative to the solution), skipped in files that contain <see cref="Unless"/>.</summary>
internal sealed record RenameRule(string Find, string Replace, string[] Files, string? Unless = null, string? Description = null);
