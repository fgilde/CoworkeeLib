using Coworkee.BackgroundJobs;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.ClientEntities;

/// <summary>Deletes touches older than <see cref="ClientEntityOptions.ChangeLogLifetime"/>; a copy last refreshed before that downloads again.</summary>
internal sealed class ClientEntityTouchCleanup(CoworkeeDbContext db, TimeProvider clock, IOptions<ClientEntityOptions> options) : IRecurringJob
{
    public const string Id = "coworkee-client-entity-touches";
    public const string Cron = "41 3 * * *";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow() - options.Value.ChangeLogLifetime;
        await db.Set<ClientEntityTouch>().Where(t => t.TouchedAt < before).OrderBy(t => t.TouchedAt).ExecuteDeleteInBatchesAsync(cancellationToken);
    }
}
