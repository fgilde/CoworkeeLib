using Coworkee.Contracts.Ai;
using Coworkee.Domain;

namespace Coworkee.Ai;

[NotAudited]
public sealed class AiToolCall : AggregateRoot
{
    public Guid? TenantId { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>The API key an MCP client called with, otherwise null.</summary>
    public Guid? ApiKeyId { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Channel")]
    public required string Channel { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Tool")]
    public required string Tool { get; set; }

    public required string Input { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Succeeded")]
    public bool Succeeded { get; set; }

    public string? Error { get; set; }

    public int DurationMs { get; set; }

    public DateTimeOffset At { get; set; }

    public static AiToolCallDto ToDto(AiToolCall c, string? output = null) =>
        new(c.Id, c.Tool, c.Channel, c.Input, c.Succeeded, c.Error, c.DurationMs, c.At, c.UserId, output, c.ApiKeyId);
}
