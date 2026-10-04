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

/// <summary>One turn of a conversation; <see cref="Role"/> is "user" or "assistant".</summary>
public sealed record ChatMessageDto(string Role, string Text);

public sealed record ChatRequest(IReadOnlyList<ChatMessageDto> Messages);

public sealed record ChatResponseDto(string Text, string StopReason, IReadOnlyList<AiToolCallDto> ToolCalls);

public sealed record AiToolDto(string Name, string Description);

public sealed record AiToolCallDto(
    Guid Id, string Tool, string Channel, string Input, bool Succeeded, string? Error, int DurationMs, DateTimeOffset At, Guid? UserId, string? Output = null);
