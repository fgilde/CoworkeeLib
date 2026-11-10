namespace Coworkee.Contracts.Search;

public sealed record GlobalSearchProviderDto(string Name, string Group, string? Icon)
{
    /// <summary>How a client finds the same hits in its local copy of the set (client entities); null when it cannot rebuild them.</summary>
    public GlobalSearchLocalDto? Local { get; init; }
}

/// <summary>
/// What a client needs to search its local copy of an OData set like the server's global search: the searched fields and the fields of title and subtitle
/// as OData paths, and the link of a hit as a template with <c>{Path}</c> placeholders (<c>/catalog/products/{Id}</c>), else the list page it opens searched for the title.
/// </summary>
public sealed record GlobalSearchLocalDto(string EntitySet, IReadOnlyList<string> Fields, string TitleField)
{
    public string? SubtitleField { get; init; }

    public string? Url { get; init; }

    public string? Page { get; init; }
}
