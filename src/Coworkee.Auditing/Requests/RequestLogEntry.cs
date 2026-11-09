using Coworkee.Domain;
using Nextended.Core.Facets;

namespace Coworkee.Auditing.Requests;

/// <summary>An API request of the request log; headers are JSON objects, bodies redacted text (JSON and forms only, cut at the size set).</summary>
[NotAudited]
public sealed class RequestLogEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset At { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? UserId { get; set; }

    /// <summary>Who was signed in as <see cref="UserId"/> ("Log in as").</summary>
    public Guid? ImpersonatorId { get; set; }

    public string? ClientId { get; init; }

    public Guid? ApiKeyId { get; init; }

    [ProvideFacet(Label = "Method")]
    public required string Method { get; init; }

    public required string Path { get; init; }

    public string? QueryString { get; set; }

    /// <summary>The route template of the endpoint.</summary>
    [ProvideFacet(Label = "Route")]
    public string? Route { get; init; }

    /// <summary>The request the endpoint dispatched (<c>IDispatcher</c>).</summary>
    public string? RequestType { get; init; }

    [ProvideFacet(Label = "Status")]
    public int StatusCode { get; init; }

    public int DurationMs { get; init; }

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }

    public string? TraceId { get; init; }

    public string? Exception { get; init; }

    public string? RequestHeaders { get; set; }

    public string? ResponseHeaders { get; set; }

    public string? RequestBody { get; set; }

    public string? ResponseBody { get; set; }
}
