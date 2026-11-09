namespace Coworkee.Contracts.Auditing;

/// <summary>
/// One API request of the request log, without headers and bodies. <paramref name="Route"/> is the route template, <paramref name="RequestType"/> the
/// request the endpoint dispatched; <paramref name="Exception"/> summarises an unhandled exception.
/// </summary>
public sealed record RequestLogDto(
    Guid Id, DateTimeOffset At, Guid? TenantId, Guid? UserId, Guid? ImpersonatorId, string? ClientId, Guid? ApiKeyId, string Method, string Path, string? QueryString,
    string? Route, string? RequestType, int StatusCode, int DurationMs, string? Ip, string? UserAgent, string? TraceId, string? Exception);

/// <summary>A request with its headers (redacted) and, for holders of <see cref="AuditPermissions.RequestBodies"/>, its bodies; <paramref name="BodiesHidden"/> otherwise.</summary>
public sealed record RequestLogDetailDto(
    RequestLogDto Request, IReadOnlyDictionary<string, string> RequestHeaders, IReadOnlyDictionary<string, string> ResponseHeaders, string? RequestBody, string? ResponseBody,
    bool BodiesHidden);
