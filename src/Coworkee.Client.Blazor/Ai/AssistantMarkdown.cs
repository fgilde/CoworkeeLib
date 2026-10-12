using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>
/// Markdown of the answers, which may echo content anyone wrote: raw HTML stays text and images become links, so a planted address loads
/// nothing on its own. Links of other sites lose query and fragment (where data would ride along) and show their host; app links stay as
/// they are; mail links keep the address only; everything else goes nowhere.
/// </summary>
public static class AssistantMarkdown
{
    public static MarkdownPipeline Pipeline { get; } = Build();

    /// <summary>The address a link may go to, and the host to show next to it for other sites.</summary>
    public static (string Url, string? Host) Safe(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return (string.Empty, null);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || url.StartsWith('/'))
        {
            var relative = !url.Contains(':', StringComparison.Ordinal) && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\', StringComparison.Ordinal);
            return (relative ? url : "#", null);
        }

        return uri.Scheme switch
        {
            "http" or "https" => (uri.GetLeftPart(UriPartial.Path), uri.Host),
            "mailto" => ("mailto:" + uri.UserInfo + "@" + uri.Host, null),
            _ => ("#", null),
        };
    }

    private static MarkdownPipeline Build()
    {
        var builder = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseAutoLinks().UseTaskLists().DisableHtml();
        builder.DocumentProcessed += Sanitize;
        return builder.Build();
    }

    private static void Sanitize(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            link.IsImage = false;
            (link.Url, var host) = Safe(link.Url);
            if (host is not null && !link.IsAutoLink)
            {
                link.InsertAfter(new LiteralInline($" ({host})"));
            }
        }

        foreach (var link in document.Descendants<AutolinkInline>().ToList())
        {
            link.Url = Safe(link.Url).Url;
        }
    }
}
