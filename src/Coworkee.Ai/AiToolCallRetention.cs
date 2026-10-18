using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai;

internal sealed class AiToolCallRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "ai-tool-calls";

    public string Title => "AI tool calls";

    public string? Description => "The audit of the tools the assistant and MCP clients called.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(c => c.At).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<AiToolCall> Older(DateTimeOffset before) => db.Set<AiToolCall>().IgnoreQueryFilters().Where(c => c.At < before);
}
