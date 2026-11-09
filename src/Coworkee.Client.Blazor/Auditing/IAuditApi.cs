using Coworkee.Contracts.Auditing;

namespace Coworkee.Client.Blazor.Auditing;

/// <summary>The request log entry with headers and bodies and the trace viewer; the lists of both logs come from OData (<c>RequestLog</c>, <c>SecurityEvents</c>).</summary>
public interface IAuditApi
{
    Task<RequestLogDetailDto> GetRequestAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TraceViewerDto> GetTraceViewerAsync(CancellationToken cancellationToken = default);
}
