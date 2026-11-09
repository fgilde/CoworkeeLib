using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Results;
using Microsoft.Extensions.Options;

namespace Coworkee.Auditing;

[AiTool(Exclude = true)]
public sealed record GetTraceViewer : IQuery<Result<TraceViewerDto>>;

internal sealed class GetTraceViewerValidator : FluentValidation.AbstractValidator<GetTraceViewer>;

/// <summary>Grafana (its Tempo data source) when the app host runs it, else the Aspire dashboard: the services the app host lists under Coworkee:Services.</summary>
internal sealed class GetTraceViewerHandler(IOptions<CoworkeeServicesOptions> services, IPermissionChecker permissions) : IHandler<GetTraceViewer, Result<TraceViewerDto>>
{
    private const string Placeholder = "{traceId}";

    // Explore with the Tempo data source the app host provisions (uid "tempo"), the placeholder left readable for the client
    private static readonly string GrafanaExplore = "/explore?left=" + Uri.EscapeDataString(
        """{"datasource":"tempo","queries":[{"refId":"A","datasource":{"type":"tempo","uid":"tempo"},"queryType":"traceql","query":"{traceId}"}]}""")
        .Replace(Uri.EscapeDataString(Placeholder), Placeholder, StringComparison.Ordinal);

    public async Task<Result<TraceViewerDto>> HandleAsync(GetTraceViewer request, CancellationToken cancellationToken)
    {
        // for the readers of either log; whoever reads neither learns nothing about the tools
        if (!await permissions.IsGrantedAsync(AuditPermissions.Requests, cancellationToken) && !await permissions.IsGrantedAsync(AuditPermissions.SecurityLog, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "Reading the trace viewer needs a permission of the request or security log.");
        }

        return new TraceViewerDto(Url("grafana") is { } grafana ? grafana + GrafanaExplore
            : Url("dashboard") is { } dashboard ? $"{dashboard}/traces/detail/{Placeholder}"
            : null);
    }

    private string? Url(string name) => services.Value.TryGetValue(name, out var service) && service.Url is { Length: > 0 } url ? url.TrimEnd('/') : null;
}
