using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Auditing;

/// <summary>A link to the trace of a request in Grafana or the Aspire dashboard; the bare id without either.</summary>
public partial class TraceLink
{
    private string? _url;

    [Parameter] public string? TraceId { get; set; }

    [Inject] private TraceViewer Viewer { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    protected override async Task OnParametersSetAsync() => _url = TraceId is { Length: > 0 } id ? await Viewer.UrlAsync(id) : null;
}
