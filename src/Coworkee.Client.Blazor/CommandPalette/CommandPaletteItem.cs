namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>One entry of the command palette; it navigates to <see cref="Href"/> unless it has an own <see cref="Run"/>.</summary>
public sealed record CommandPaletteItem(string Group, string Title)
{
    public string? Icon { get; init; }

    public string? Subtitle { get; init; }

    /// <summary>More words the fuzzy search looks at, e.g. "dark light theme".</summary>
    public string? Keywords { get; init; }

    /// <summary>Where the item leads; items with a link come back under "Recent" once used.</summary>
    public string? Href { get; init; }

    public Func<Task>? Run { get; init; }

    /// <summary>The provider searched itself (e.g. an OData search): the item shows without the fuzzy filter.</summary>
    public bool Matched { get; init; }

    /// <summary>Groups sort by the smallest order of their items: pages 0, records 10, actions 20, the assistant last.</summary>
    public int Order { get; init; }
}

/// <summary>Adds items to the command palette (Ctrl+K); register with AddCommandPaletteProvider.</summary>
public interface ICommandPaletteProvider
{
    /// <summary>Items for the text typed so far, empty when the palette just opened. The palette filters items unless they are <see cref="CommandPaletteItem.Matched"/>.</summary>
    Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken);
}

/// <summary>An action of the app in the palette, e.g. "New product"; <see cref="Href"/> or <see cref="Run"/> does it.</summary>
public sealed record CommandPaletteCommand(string Title, string Icon, string? Permission = null, string? Href = null, Func<IServiceProvider, Task>? Run = null, string? Keywords = null);

/// <summary>Records of an OData set the palette finds by text; a hit opens <see cref="Page"/> searched for it unless <see cref="Href"/> says otherwise.</summary>
public sealed record CommandPaletteEntitySet<T>(string EntitySet, string Page, Func<T, string> Title, IReadOnlyCollection<string> SearchFields)
{
    public string? Permission { get; init; }

    public string? Icon { get; init; }

    /// <summary>The group heading; the entity set by default.</summary>
    public string? Group { get; init; }

    public Func<T, string?>? Subtitle { get; init; }

    public Func<T, string>? Href { get; init; }

    public int Top { get; init; } = 5;
}
