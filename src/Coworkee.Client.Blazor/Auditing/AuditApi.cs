using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Auditing;

namespace Coworkee.Client.Blazor.Auditing;

internal sealed class AuditApi(HttpClient http) : ApiClientBase(http), IAuditApi
{
    private const string Root = "api/v1/audit";

    public Task<RequestLogDetailDto> GetRequestAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<RequestLogDetailDto>($"{Root}/requests/{id}", cancellationToken);

    public Task<TraceViewerDto> GetTraceViewerAsync(CancellationToken cancellationToken = default) => GetAsync<TraceViewerDto>($"{Root}/trace-viewer", cancellationToken);
}
