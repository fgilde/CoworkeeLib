namespace Coworkee.Cli.Apps;

/// <summary>A line inserted after the first line matching <see cref="InsertAfter"/>, with that line's indentation; skipped in files that contain <see cref="Unless"/>.</summary>
internal sealed record InsertRule(string InsertAfter, string Line, string[] Files, string? Unless = null, string? Description = null);
