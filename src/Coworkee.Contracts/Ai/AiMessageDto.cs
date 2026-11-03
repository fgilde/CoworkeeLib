namespace Coworkee.Contracts.Ai;

/// <summary>
/// A message of a session. <see cref="Pending"/> lists tools the assistant wants to run that change or delete data and wait for the
/// user; <see cref="Decision"/> is null while waiting, then "approved" or "declined".
/// </summary>
public sealed record AiMessageDto(
    Guid Id, string Role, string Text, IReadOnlyList<AiToolCallDto> ToolCalls, IReadOnlyList<AiPendingToolDto> Pending, string? Decision, DateTimeOffset At);
