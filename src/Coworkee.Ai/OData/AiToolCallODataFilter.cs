using Coworkee.Core.Security;
using Coworkee.OData;

namespace Coworkee.Ai.OData;

internal sealed class AiToolCallODataFilter(ICurrentUser currentUser) : IODataEntityFilter<AiToolCall>
{
    public Task<IQueryable<AiToolCall>> ApplyAsync(IQueryable<AiToolCall> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(c => c.TenantId == currentUser.TenantId));
}
