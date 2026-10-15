namespace Coworkee.OData;

public sealed class CoworkeeODataOptions
{
    public const string Section = "Coworkee:OData";

    public int MaxTop { get; set; } = 1000;

    public int MaxExportRows { get; set; } = 100_000;

    /// <summary>Exports of more rows run as a background job; the user gets a download link when the file is ready.</summary>
    public int BackgroundExportRows { get; set; } = 10_000;
}
