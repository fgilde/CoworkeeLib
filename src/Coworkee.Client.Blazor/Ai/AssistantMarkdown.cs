using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>
/// Markdown of the answers, which may echo content anyone wrote: raw HTML stays text, links go only to http(s), mail or the app, and
/// images become links, so a planted image address cannot carry data away without a click.
/// </summary>
public static class AssistantMarkdown
{
    public static MarkdownPipeline Pipeline { get; } = Build();

    public static bool IsSafeUrl(string? url) =>
        string.IsNullOrEmpty(url)
        || (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Scheme is "http" or "https" or "mailto" && !url.StartsWith("//", StringComparison.Ordinal)
            : !url.Contains(':', StringComparison.Ordinal) && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\', StringComparison.Ordinal));

    private static MarkdownPipeline Build()
    {
        var builder = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseAutoLinks().UseTaskLists().DisableHtml();
        builder.DocumentProcessed += Sanitize;
        return builder.Build();
    }

    private static void Sanitize(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>())
        {
            link.IsImage = false;
            link.Url = IsSafeUrl(link.Url) ? link.Url : "#";
        }

        foreach (var link in document.Descendants<AutolinkInline>())
        {
            link.Url = IsSafeUrl(link.Url) ? link.Url : "#";
        }
    }
}
