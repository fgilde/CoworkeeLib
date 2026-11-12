namespace Coworkee.Contracts.Ai;

/// <summary>
/// The browser's answer to an <see cref="AiStreamEvent.ClientQuery"/>: <see cref="Output"/> is the tool result in the server tool's shape, null when the
/// local copy cannot answer it (the server then runs the tool itself).
/// </summary>
public sealed record AiClientResult(string CallId, string? Output);
