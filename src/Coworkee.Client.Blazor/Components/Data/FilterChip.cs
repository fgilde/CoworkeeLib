namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A column filter as a chip: the column's key and title and the filter as text.</summary>
public sealed record FilterChip(string Key, string Label, string Text);
