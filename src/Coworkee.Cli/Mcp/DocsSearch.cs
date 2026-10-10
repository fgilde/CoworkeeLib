using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Coworkee.Cli.Mcp;

/// <summary>Searches the published search index of the documentation; offline, the page titles.</summary>
internal static class DocsSearch
{
    public const string Site = "https://fgilde.github.io/CoworkeeLib/";

    // ponytail: copy of the English nav in website/mkdocs.yml, only used offline; a test keeps it in line
    public static readonly IReadOnlyList<(string Title, string Path)> Pages =
    [
        ("Home", ""),
        ("Installation", "getting-started/installation/"),
        ("Command line", "getting-started/cli/"),
        ("MCP server of the CLI", "getting-started/mcp/"),
        ("Solution structure", "getting-started/solution-structure/"),
        ("Your first feature", "getting-started/first-feature/"),
        ("Modules", "fundamentals/modules/"),
        ("Messaging and pipeline", "fundamentals/messaging/"),
        ("Persistence", "fundamentals/persistence/"),
        ("DTOs and mapping", "fundamentals/dtos/"),
        ("Events and locks", "fundamentals/events/"),
        ("HTTP APIs", "web/http-api/"),
        ("Application services", "web/application-services/"),
        ("OData and facets", "web/odata/"),
        ("Response filters", "web/response-filters/"),
        ("SDKs", "web/sdk/"),
        ("Permissions", "security/permissions/"),
        ("Authentication and Keycloak", "security/authentication/"),
        ("Clients, sessions and passwords", "security/clients/"),
        ("Registration and sign-in rules", "security/registration/"),
        ("Sign-in pages and designs", "security/sign-in-pages/"),
        ("Privacy and personal data", "security/privacy/"),
        ("Client and layout", "ui/client/"),
        ("Data tables", "ui/data-table/"),
        ("Client entities", "ui/client-entities/"),
        ("Replacing components", "ui/customizing/"),
        ("Settings and app configuration", "modules/settings/"),
        ("Features and editions", "modules/features/"),
        ("Mailing", "modules/mailing/"),
        ("Background jobs", "modules/background-jobs/"),
        ("File storage", "modules/storage/"),
        ("Files", "modules/files/"),
        ("Search", "modules/search/"),
        ("Realtime and notifications", "modules/realtime/"),
        ("Theming", "modules/theming/"),
        ("Auditing", "modules/auditing/"),
        ("Localization", "modules/localization/"),
        ("Database backups", "modules/backups/"),
        ("Chat", "modules/chat/"),
        ("Extended attributes", "modules/extended-attributes/"),
        ("Comments, tags and ratings", "modules/social/"),
        ("AI assistant and MCP", "modules/ai/"),
        ("Aspire app host", "hosting/aspire/"),
        ("Testing", "testing/"),
        ("Packages and namespaces", "reference/packages/"),
    ];

    public static async Task<string> SearchAsync(string query, HttpClient http, CancellationToken cancellationToken)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            var index = await http.GetFromJsonAsync<SearchIndex>(Site + "search/search_index.json", cancellationToken);
            var hits = index!.Docs
                .Where(d => !d.Location.StartsWith("de/", StringComparison.Ordinal))
                .Select(d => (Doc: d, Score: terms.Sum(t => (Count(d.Title, t) * 5) + Count(d.Text, t))))
                .Where(h => h.Score > 0)
                .OrderByDescending(h => h.Score)
                .Take(8)
                .Select(h => $"{h.Doc.Title} - {Site}{h.Doc.Location}\n  {Snippet(h.Doc.Text, terms)}")
                .ToList();
            return hits.Count > 0 ? string.Join("\n", hits) : $"Nothing found for '{query}'.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return Offline(terms);
        }
    }

    public static string Offline(IReadOnlyList<string> terms)
    {
        var pages = Pages.Where(p => terms.Count == 0 || terms.Any(t => p.Title.Contains(t, StringComparison.OrdinalIgnoreCase) || p.Path.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
        return "The search index is not reachable; pages by title:\n"
            + string.Join("\n", (pages.Count > 0 ? pages : Pages).Select(p => $"{p.Title} - {Site}{p.Path}"));
    }

    private static int Count(string text, string term)
    {
        var count = 0;
        for (var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); index >= 0; index = text.IndexOf(term, index + term.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    private static string Snippet(string text, IReadOnlyList<string> terms)
    {
        var at = terms.Select(t => text.IndexOf(t, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, at - 60);
        return text.Substring(start, Math.Min(200, text.Length - start)).ReplaceLineEndings(" ");
    }

    private sealed record SearchIndex([property: JsonPropertyName("docs")] IReadOnlyList<SearchDoc> Docs);

    private sealed record SearchDoc(
        [property: JsonPropertyName("location")] string Location,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("text")] string Text);
}
