namespace Coworkee.OData;

public sealed class CoworkeeODataOptions
{
    public const string Section = "Coworkee:OData";

    public int MaxTop { get; set; } = 1000;

    public int MaxExportRows { get; set; } = 100_000;
}
