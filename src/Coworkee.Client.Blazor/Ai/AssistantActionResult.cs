namespace Coworkee.Client.Blazor.Ai;

/// <summary>What a client action did, shown as a note under the answer, e.g. "Switched to dark mode".</summary>
public sealed record AssistantActionResult(bool Succeeded, string Note);
