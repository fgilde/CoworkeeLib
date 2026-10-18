using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Persistence;

public static class BatchDeleteExtensions
{
    public const int BatchSize = 1000;

    /// <summary>Deletes the rows of an ordered query <see cref="BatchSize"/> at a time, so no statement locks a large table for long; skips the change tracker.</summary>
    public static async Task<long> ExecuteDeleteInBatchesAsync<T>(this IOrderedQueryable<T> query, CancellationToken cancellationToken)
    {
        long total = 0;
        int deleted;
        do
        {
            deleted = await query.Take(BatchSize).ExecuteDeleteAsync(cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize);

        return total;
    }
}
