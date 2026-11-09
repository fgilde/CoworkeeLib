namespace Coworkee.Contracts.Auditing;

/// <summary>Where a trace id opens: <paramref name="UrlTemplate"/> holds <c>{traceId}</c> (Grafana's Tempo, else the Aspire dashboard); null without either.</summary>
public sealed record TraceViewerDto(string? UrlTemplate);
