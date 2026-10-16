using System.Runtime.CompilerServices;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Search;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

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

public static class IndexGlobalSearchServiceCollectionExtensions
{
    /// <summary>Lets the global search find documents of an index alias, e.g. <c>services.AddGlobalSearchIndex("products", s =&gt; s.Fields("name", "barcode").Url("/catalog/products?id={id}"))</c>.</summary>
    public static IServiceCollection AddGlobalSearchIndex(this IServiceCollection services, string alias, Action<IndexSearchOptions> configure)
    {
        var options = new IndexSearchOptions();
        configure(options);
        if (options.SearchFields.Count == 0)
        {
            throw new ArgumentException($"The global search of the index {alias} needs at least one field.", nameof(configure));
        }

        return services.AddGlobalSearchProvider(new IndexSearchProvider(alias, options));
    }
}

/// <summary>Full text hits of the index; scores are scaled by the best hit, a match of the title by the shared rules counts when it is higher.</summary>
internal sealed class IndexSearchProvider(string alias, IndexSearchOptions options) : IGlobalSearchProvider
{
    public string Name => options.ProviderName ?? alias.ToLowerInvariant();

    public string Group => options.GroupName ?? alias;

    public string? Icon => options.IconName;

    public string? Permission => options.RequiredPermission;

    public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var filters = new List<SearchFilter>();
        if (options.TenantFieldName is { } tenantField)
        {
            if (context.Services.GetRequiredService<ICurrentUser>().TenantId is not { } tenant)
            {
                yield break;
            }

            filters.Add(new SearchFilter(tenantField, FilterOperator.Equals, [tenant.ToString()]));
        }

        if (options.RowFilters is { } rowFilters)
        {
            filters.AddRange(await rowFilters(context.Services, cancellationToken));
        }

        var titleField = options.TitleField ?? options.SearchFields[0];
        var result = await context.Services.GetRequiredService<ISearchIndex>().SearchAsync(alias, new SearchQuery
        {
            Term = query.Text,
            Exact = query.Exact,
            TextFields = options.SearchFields,
            Filters = filters,
            Size = context.Limit,
            Include = [.. new[] { titleField, options.SubtitleField }.OfType<string>().Distinct()],
        }, cancellationToken);
        var best = result.Hits.Max(h => h.Score) is > 0 and { } top ? top : 1;
        foreach (var hit in result.Hits)
        {
            var title = hit.Fields?[titleField]?.ToString() ?? hit.Id;
            var match = SearchMatch.Score(title, query);
            yield return new GlobalSearchHit(Name, Group, title)
            {
                Subtitle = options.SubtitleField is { } subtitle ? hit.Fields?[subtitle]?.ToString() : null,
                Url = options.UrlTemplate?.Replace("{id}", Uri.EscapeDataString(hit.Id), StringComparison.Ordinal),
                Id = hit.Id,
                Score = Math.Max(match?.Score ?? 0, 0.7 * (hit.Score ?? 0) / best),
                Highlights = match?.Highlights ?? [],
            };
        }
    }
}
