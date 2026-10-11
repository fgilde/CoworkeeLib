using System.Text.Json;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>Reads and writes queries as compact JSON: enums as names, empty values left out, so saved queries survive reordered enums.</summary>
public static class QueryJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

    public static string Serialize(QueryGroup query, bool indented = false) => JsonSerializer.Serialize(query, indented ? Indented : Options);

    /// <summary>The query, or <c>null</c> when the text is not a query (links and pasted text can be anything).</summary>
    public static QueryGroup? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QueryGroup>(json, Options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A deep copy, e.g. to edit a query without touching the one in use.</summary>
    public static QueryGroup Clone(QueryGroup query) => Deserialize(Serialize(query))!;
}
