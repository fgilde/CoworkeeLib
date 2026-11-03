namespace Coworkee.Cli.Apps;

/// <summary>One file of the plan, by its path relative to the solution with forward slashes.</summary>
internal sealed record MergeStep(string Path, MergeAction Action, string Note, byte[]? Content = null)
{
    public const string RejectSuffix = ".rej";

    public override string ToString() => $"{Action.ToString().ToLowerInvariant(),-8} {Path}{(Note.Length > 0 ? "  (" + Note + ")" : string.Empty)}";
}
