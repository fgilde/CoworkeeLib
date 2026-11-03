namespace Coworkee.Search;

public enum SearchFieldType
{
    /// <summary>Exact values: ids, keys, list values; filterable, sortable, facetable; also matched by full text.</summary>
    Keyword,

    /// <summary>Analysed text; a keyword sub field keeps exact filters and sorting working.</summary>
    Text,
    Date,
    Long,
    Double,
    Boolean,
}
