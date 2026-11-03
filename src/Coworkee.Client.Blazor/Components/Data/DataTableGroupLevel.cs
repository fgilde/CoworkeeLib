namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A grouping column: its property path, its title and whether its groups are listed descending.</summary>
internal sealed record DataTableGroupLevel(string Path, string Title, bool Descending);
