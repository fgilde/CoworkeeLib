namespace Coworkee.Contracts.Ai;

public sealed record AiToolCallDto(
    Guid Id, string Tool, string Channel, string Input, bool Succeeded, string? Error, int DurationMs, DateTimeOffset At, Guid? UserId, string? Output = null,
    Guid? ApiKeyId = null);
