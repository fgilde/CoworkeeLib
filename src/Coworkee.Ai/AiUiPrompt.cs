using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

/// <summary>
/// What the client says the user sees (<see cref="AiUiContext"/>), turned into a short part of the system prompt and into the client actions
/// the assistant may ask for. It goes with every round, so it stays within <see cref="MaxLength"/>.
/// </summary>
internal static partial class AiUiPrompt
{
    public const int MaxLength = 4_000;

    public const int MaxActions = 20;

    private const int MaxText = 100;

    private const int MaxDescription = 120;

    private const int MaxListed = 30;

    private const int MaxSchema = 4_000;

    /// <summary>The result the model gets for a client action: the client runs it, the server only passes it on.</summary>
    public const string Requested = "Requested on the client; it runs in the user's browser right away. Tell the user briefly what changed.";

    private const string Instructions =
        """
        The client sent what the signed-in user sees. When the user asks how to do something or where something is, answer with a concrete click path
        built from the menu titles below, e.g. "Administration > System > Themes", and offer to open the page. Name only pages, menus and actions listed here
        or given to you as tools: the user cannot see any others. The client tools (navigation, appearance, language) run in the browser at once, without
        a confirmation; use them when the user asks for it.

        """;

    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    public static string Build(AiUiContext? ui)
    {
        if (ui is null)
        {
            return string.Empty;
        }

        var text = new StringBuilder("\n\n## The user interface\n").Append(Instructions);
        if (ui.Current is { } current)
        {
            text.Append("Current page: ").Append(Clip(current.Title, MaxText)).Append(" (").Append(Clip(current.Href, MaxText)).Append(")\n");
        }

        if (ui.Themes is { Count: > 0 } themes)
        {
            text.Append("Themes: ").Append(List(themes)).Append(ui.Theme is { } theme ? $" (current: {Clip(theme, MaxText)})" : string.Empty).Append('\n');
        }

        if (ui.Mode is { } mode)
        {
            text.Append("Color mode: ").Append(Clip(mode, MaxText)).Append('\n');
        }

        if (ui.Languages is { Count: > 0 } languages)
        {
            text.Append("Languages: ").Append(List(languages)).Append(ui.Language is { } language ? $" (current: {Clip(language, MaxText)})" : string.Empty).Append('\n');
        }

        text.Append("Right to left: ").Append(ui.RightToLeft ? "on" : "off").Append('\n');
        if (ui.Pages is { Count: > 0 } pages)
        {
            text.Append("Pages the user can open (menu path > title (address): description):\n");
            foreach (var page in pages)
            {
                var line = new StringBuilder("- ");
                if (page.Group is { Length: > 0 } group)
                {
                    line.Append(Clip(group, MaxText)).Append(" > ");
                }

                line.Append(Clip(page.Title, MaxText)).Append(" (").Append(Clip(page.Href, MaxText)).Append(')');
                if (page.Description is { Length: > 0 } description)
                {
                    line.Append(": ").Append(Clip(description, MaxDescription));
                }

                if (text.Length + line.Length > MaxLength)
                {
                    text.Append("- (more pages not listed)\n");
                    break;
                }

                text.Append(line).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The client actions the client declared, without bad names, names of server tools or oversized schemas.</summary>
    public static IReadOnlyList<AiClientActionDto> Actions(AiUiContext? ui, IEnumerable<AiTool> tools)
    {
        var taken = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        return [.. (ui?.Actions ?? [])
            .Where(a => a.Name is { } name && ToolName().IsMatch(name) && !taken.Contains(name))
            .DistinctBy(a => a.Name)
            .Select(a => a with
            {
                Description = Clip(a.Description ?? string.Empty, 500),
                Parameters = a.Parameters is { ValueKind: JsonValueKind.Object } schema && schema.GetRawText().Length <= MaxSchema ? schema : EmptySchema,
            })
            .Take(MaxActions)];
    }

    private static string List(IEnumerable<string> items) => string.Join(", ", items.Take(MaxListed).Select(i => Clip(i, MaxText)));

    // one line each: the values come from the client and must not reshape the prompt
    private static string Clip(string text, int length)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length > length ? line[..length].TrimEnd() + "…" : line;
    }

    // the tool names the providers accept
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ToolName();
}
