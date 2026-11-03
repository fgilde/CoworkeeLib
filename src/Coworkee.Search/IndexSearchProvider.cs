using System.Runtime.CompilerServices;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Search;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Search;

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
