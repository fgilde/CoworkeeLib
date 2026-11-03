namespace Coworkee.Search;

/// <summary>The fields of one index; fields not listed are stored but not searchable.</summary>
public sealed record SearchSchema(string Alias, IReadOnlyList<SearchField> Fields);
