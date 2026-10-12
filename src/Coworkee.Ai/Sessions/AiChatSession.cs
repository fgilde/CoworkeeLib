using System.Text.Json;
using Coworkee.Contracts.Ai;
using Coworkee.Domain;

namespace Coworkee.Ai.Sessions;

/// <summary>A conversation of a user with the assistant; it follows the user to every tab and device.</summary>
[NotAudited]
public sealed class AiChatSession : Entity, IMultiTenant, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<AiChatMessage> Messages { get; set; } = [];

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];

    public AiSessionDto ToDto() => new(Id, Title, UpdatedAt);
}

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

/// <summary>A tool call of a round with a tool that needs confirmation: <see cref="Output"/> is null until the user decided.</summary>
internal sealed record AiPendingCall(string CallId, string Tool, string Arguments, string Input, string? Output, bool Succeeded, bool Destructive);
