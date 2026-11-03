using System.Text.Json;
using Coworkee.Contracts.Ai;
using Coworkee.Domain;

namespace Coworkee.Ai.Sessions;

[NotAudited]
public sealed class AiChatMessage : Entity
{
    public Guid SessionId { get; set; }

    /// <summary>"user" or "assistant".</summary>
    public required string Role { get; set; }

    public required string Text { get; set; }

    /// <summary>The tool calls of the answer as JSON.</summary>
    public string? ToolCalls { get; set; }

    /// <summary>The tool round that waits for the user's confirmation (<see cref="AiPendingState"/>, its calls encrypted).</summary>
    public string? Pending { get; set; }

    public string? Decision { get; set; }

    public DateTimeOffset At { get; set; }

    public AiMessageDto ToDto() => new(
        Id, Role, Text,
        ToolCalls is null ? [] : JsonSerializer.Deserialize<List<AiToolCallDto>>(ToolCalls, JsonSerializerOptions.Web) ?? [],
        Pending is null ? [] : AiPendingProtector.Read(Pending).Tools,
        Decision,
        At);
}
