namespace Coworkee.OData.Transfer;

public sealed record DataSetInfo(string Name, IReadOnlyDictionary<string, string> Fields);
