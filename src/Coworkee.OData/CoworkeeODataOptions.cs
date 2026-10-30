namespace Coworkee.OData;

public sealed class CoworkeeODataOptions
{
    public const string Section = "Coworkee:OData";

    public int MaxTop { get; set; } = 1000;

    /// <summary>
    /// Nodes a <c>$filter</c> or <c>$orderby</c> may have (each operator, property, literal and function call counts): a search that ORs
    /// <c>contains</c> over 20 fields exceeds 100. Larger limits let users send more expensive queries.
    /// </summary>
    public int MaxNodeCount { get; set; } = 100;

    /// <summary>How deep <c>any</c>/<c>all</c> may nest in <c>$filter</c>.</summary>
    public int MaxAnyAllExpressionDepth { get; set; } = 1;

    /// <summary>How deep <c>$expand</c> may nest; 0 allows any depth.</summary>
    public int MaxExpansionDepth { get; set; } = 2;

    /// <summary>Properties <c>$orderby</c> may sort by.</summary>
    public int MaxOrderByNodeCount { get; set; } = 5;

    /// <summary>
    /// The service document (<c>/odata</c>) and <c>$metadata</c> of every route answer without sign-in. Off by default: both name every set and property
    /// of the model, for any role.
    /// </summary>
    public bool AnonymousMetadata { get; set; }

    public int MaxExportRows { get; set; } = 100_000;

    /// <summary>Exports of more rows run as a background job; the user gets a download link when the file is ready.</summary>
    public int BackgroundExportRows { get; set; } = 10_000;

    /// <summary>Property, set and function names in URIs match regardless of case (<c>$filter=name eq 'x'</c>, <c>$orderby=price</c>), e.g. for camelCase clients.</summary>
    public bool CaseInsensitive { get; set; } = true;

    /// <summary>
    /// The model takes keys and properties from the EF model of each set's DbContext (external ones too) instead of the CLR conventions: the EF primary
    /// key of every type the sets reach (a <c>{Type}Id</c> property is no key then), and properties without a public setter EF maps. Off by default,
    /// because it adds properties that were not in the model before; hide what users must not see.
    /// </summary>
    public bool ModelFromEntityFramework { get; set; }

    /// <summary>Running and queued exports one user may have at a time; another streamed export answers 429.</summary>
    public int MaxConcurrentExports { get; set; } = 2;

    /// <summary>A streamed export is cancelled after this; running or queued exports older than this count as stale and fail.</summary>
    public TimeSpan ExportTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
