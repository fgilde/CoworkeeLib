namespace Coworkee.Client.Blazor.Data;

/// <summary>Rows of a local copy by field name and how many rows match in all: the shape of the server's query_data, so the assistant gets the same either way.</summary>
public sealed record LocalRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, long Total);
