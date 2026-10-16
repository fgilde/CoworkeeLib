using System.ComponentModel;
using Coworkee.Application.GlobalSearch;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Search;
using Coworkee.Core.Results;

namespace Coworkee.OData.GlobalSearch;

/// <param name="Query">Text to find; <c>"..."</c> for exact values, <c>users:max</c> to search one type only, a GUID for one record.</param>
/// <param name="Types">Provider names to search in, e.g. users, roles, products; empty searches all the user may read.</param>
/// <param name="Limit">Hits per provider, 1 to 50.</param>
[AiTool("Finds records of every kind the user may read (users, roles, files, reports, chats and the app's own data) by text; each hit has a title, a link and its entity set and id.",
    Name = "global_search")]
public sealed record RunGlobalSearch(
    [property: Description("Text to find; quote it for exact values, prefix a type like users:max to search one type")] string Query,
    [property: Description("Provider names to search in, e.g. users, roles, products; empty searches all")] IReadOnlyList<string>? Types = null,
    [property: Description("Hits per provider, 1 to 50")] int? Limit = null) : IQuery<Result<GlobalSearchResult>>;

internal sealed class RunGlobalSearchHandler(GlobalSearcher searcher) : IHandler<RunGlobalSearch, Result<GlobalSearchResult>>
{
    public async Task<Result<GlobalSearchResult>> HandleAsync(RunGlobalSearch query, CancellationToken cancellationToken) =>
        await searcher.SearchAsync(query.Query, query.Types?.ToList(), query.Limit, cancellationToken);
}
