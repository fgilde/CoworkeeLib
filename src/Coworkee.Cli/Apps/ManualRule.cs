namespace Coworkee.Cli.Apps;

/// <summary>A step the person takes; <see cref="Find"/> locates the code it concerns.</summary>
internal sealed record ManualRule(string Text, string Find, string[] Files, string? Unless = null);
