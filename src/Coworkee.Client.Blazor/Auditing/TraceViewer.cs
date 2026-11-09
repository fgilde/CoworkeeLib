namespace Coworkee.Client.Blazor.Auditing;

/// <summary>Asks once per session where traces open; null without Grafana or the Aspire dashboard.</summary>
public sealed class TraceViewer(IAuditApi api)
{
    private Task<string?>? _template;

    public async Task<string?> UrlAsync(string traceId)
    {
        _template ??= LoadAsync();
        return await _template is { } template ? template.Replace("{traceId}", Uri.EscapeDataString(traceId), StringComparison.Ordinal) : null;
    }

    private async Task<string?> LoadAsync()
    {
        try
        {
            return (await api.GetTraceViewerAsync()).UrlTemplate;
        }
        catch (Api.ApiException)
        {
            return null;
        }
    }
}
