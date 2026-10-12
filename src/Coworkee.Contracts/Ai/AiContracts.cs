namespace Coworkee.Contracts.Ai;

public static class AiPermissions
{
    public const string GroupName = "Ai";

    public const string Chat = "Ai.Chat";

    public const string Audit = "Ai.Audit";
}

public static class AiChannels
{
    public const string Chat = "chat";

    public const string Mcp = "mcp";
}

public sealed record AiToolDto(string Name, string Description);

public sealed record AiToolCallDto(
    Guid Id, string Tool, string Channel, string Input, bool Succeeded, string? Error, int DurationMs, DateTimeOffset At, Guid? UserId, string? Output = null,
    Guid? ApiKeyId = null);

/// <summary>Whether the assistant can answer the current user: switched on, configured and permitted.</summary>
public sealed record AiStatusDto(bool Available);

public sealed record AiSessionDto(Guid Id, string Title, DateTimeOffset UpdatedAt);

public sealed record AiSessionDetailDto(Guid Id, string Title, DateTimeOffset UpdatedAt, IReadOnlyList<AiMessageDto> Messages);

/// <summary>
/// A message of a session. <see cref="Pending"/> lists tools the assistant wants to run that change or delete data and wait for the
/// user; <see cref="Decision"/> is null while waiting, then "approved" or "declined".
/// </summary>
public sealed record AiMessageDto(
    Guid Id, string Role, string Text, IReadOnlyList<AiToolCallDto> ToolCalls, IReadOnlyList<AiPendingToolDto> Pending, string? Decision, DateTimeOffset At);

public sealed record AiPendingToolDto(string Tool, string Input);

public sealed record AiSessionRequest(string? Title);

public sealed record AiSendRequest(string Text);

public sealed record AiConfirmRequest(bool Approve);

/// <summary>One line of the streamed answer (application/x-ndjson): text as it arrives, each tool call, then the saved message or an error.</summary>
public sealed record AiStreamEvent(string Type, string? Text = null, AiToolCallDto? ToolCall = null, AiMessageDto? Message = null)
{
    public const string Delta = "delta";

    public const string Tool = "tool";

    public const string Done = "done";

    public const string Error = "error";
}

public static class AiDecisions
{
    public const string Approved = "approved";

    public const string Declined = "declined";
}
