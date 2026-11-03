namespace Coworkee.Search;

/// <summary>How the global search reads one alias of the search index (Elasticsearch).</summary>
public sealed class IndexSearchOptions
{
    internal List<string> SearchFields { get; } = [];

    internal string? TitleField { get; private set; }

    internal string? SubtitleField { get; private set; }

    internal string? UrlTemplate { get; private set; }

    internal string? ProviderName { get; private set; }

    internal string? GroupName { get; private set; }

    internal string? IconName { get; private set; }

    internal string? RequiredPermission { get; private set; }

    internal string? TenantFieldName { get; private set; } = "tenantId";

    internal Func<IServiceProvider, CancellationToken, Task<IEnumerable<SearchFilter>>>? RowFilters { get; private set; }

    /// <summary>The text fields searched; the first is the title unless <see cref="Title"/> says otherwise.</summary>
    public IndexSearchOptions Fields(params string[] fields) => Set(() => SearchFields.AddRange(fields));

    public IndexSearchOptions Title(string field) => Set(() => TitleField = field);

    public IndexSearchOptions Subtitle(string field) => Set(() => SubtitleField = field);

    /// <summary>Where a hit leads; <c>{id}</c> is replaced, e.g. <c>/catalog/products?id={id}</c>.</summary>
    public IndexSearchOptions Url(string template) => Set(() => UrlTemplate = template);

    public IndexSearchOptions Name(string name) => Set(() => ProviderName = name.ToLowerInvariant());

    public IndexSearchOptions Group(string group) => Set(() => GroupName = group);

    public IndexSearchOptions Icon(string icon) => Set(() => IconName = icon);

    public IndexSearchOptions Permission(string permission) => Set(() => RequiredPermission = permission);

    /// <summary>The keyword field with the organisation of a document (<c>tenantId</c>); null for an index shared by all organisations.</summary>
    public IndexSearchOptions TenantField(string? field) => Set(() => TenantFieldName = field);

    /// <summary>More filters per search, e.g. the row rules of the user, since the index cannot apply the rules of the database.</summary>
    public IndexSearchOptions Where(Func<IServiceProvider, CancellationToken, Task<IEnumerable<SearchFilter>>> filters) => Set(() => RowFilters = filters);

    private IndexSearchOptions Set(Action set)
    {
        set();
        return this;
    }
}
