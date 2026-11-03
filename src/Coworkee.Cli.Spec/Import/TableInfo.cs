namespace Coworkee.Cli.Spec.Import;

public sealed class TableInfo(string schema, string name)
{
    public string Schema => schema;

    public string Name => name;

    public List<ColumnInfo> Columns { get; } = [];

    public List<string> PrimaryKey { get; } = [];

    public List<ForeignKeyInfo> ForeignKeys { get; } = [];

    /// <summary>Columns of each unique index, by index name.</summary>
    public Dictionary<string, List<string>> Uniques { get; } = [];
}
