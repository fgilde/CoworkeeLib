using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>Text, image or embedded page; nothing to query.</summary>
public partial class ContentWidget
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    // reports are shared: raw HTML in the text could run script for every viewer
    private string Markdown => (Widget.Options.Text ?? string.Empty).Replace("<", "&lt;", StringComparison.Ordinal);

    private bool Safe => Uri.TryCreate(Widget.Options.Url, UriKind.Absolute, out var url) ? url.Scheme is "https" or "http" : Widget.Options.Url is ['/', not '/', ..];

    // pages of the app itself must not get script and same origin together, they could reach out of the frame
    private string Sandbox => Uri.TryCreate(Widget.Options.Url, UriKind.Absolute, out var url) && url.Authority != new Uri(Nav.BaseUri).Authority
        ? "allow-scripts allow-same-origin allow-popups allow-forms"
        : "allow-popups";
}
