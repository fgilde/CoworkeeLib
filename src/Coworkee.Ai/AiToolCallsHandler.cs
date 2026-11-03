using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai;

internal sealed class AiToolCallsHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetAiToolCalls, Result<PagedResult<AiToolCallDto>>>
{
    public async Task<Result<PagedResult<AiToolCallDto>>> HandleAsync(GetAiToolCalls query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var calls = db.Set<AiToolCall>().AsNoTracking().Where(c => c.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            calls = calls.Where(c => c.Channel == query.Channel);
        }

        if (!string.IsNullOrWhiteSpace(query.Page.Search))
        {
            var search = query.Page.Search.Trim();
            calls = calls.Where(c => c.Tool.Contains(search));
        }

        var total = await calls.CountAsync(cancellationToken);
        var items = await calls.OrderByDescending(c => c.At)
            .Skip((query.Page.Page - 1) * query.Page.PageSize).Take(query.Page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<AiToolCallDto>([.. items.Select(c => AiToolCall.ToDto(c))], total, query.Page.Page, query.Page.PageSize);
    }
}
