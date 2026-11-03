namespace Coworkee.Cli.Apps;

/// <summary>The result: the plan, the .rej files written and whether it was applied.</summary>
internal sealed record ApplyResult(IReadOnlyList<MergeStep> Plan, IReadOnlyList<string> Rejects, bool Applied);
