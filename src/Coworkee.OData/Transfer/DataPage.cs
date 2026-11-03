namespace Coworkee.OData.Transfer;

public sealed record DataPage(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, long Total);
