using Coworkee.Application.GlobalSearch;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Search;
using Coworkee.Core.Results;

namespace Coworkee.OData.GlobalSearch;

internal sealed class RunGlobalSearchHandler(GlobalSearcher searcher) : IHandler<RunGlobalSearch, Result<GlobalSearchResult>>
{
    public async Task<Result<GlobalSearchResult>> HandleAsync(RunGlobalSearch query, CancellationToken cancellationToken) =>
        await searcher.SearchAsync(query.Query, query.Types?.ToList(), query.Limit, cancellationToken);
}
