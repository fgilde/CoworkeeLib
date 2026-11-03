namespace Coworkee.Ai.Sessions;

/// <summary>A tool call of a round with a tool that needs confirmation: <see cref="Output"/> is null until the user decided.</summary>
internal sealed record AiPendingCall(string CallId, string Tool, string Arguments, string Input, string? Output, bool Succeeded, bool Destructive);
