namespace Coworkee.OData.Transfer;

public sealed record DataSetInfo(string Name, IReadOnlyDictionary<string, string> Fields)
{
    /// <summary>The fields a filter or order can read through an index; on other fields it reads the whole table.</summary>
    public IReadOnlyList<string> Indexed { get; init; } = [];
}
