using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.ClientEntities;

/// <summary>
/// Reports rows as changed to the client entity copies when no <c>ChangedAt</c> column (<c>ModifiedAt</c>) moved, e.g. a tag put on a record
/// through a link table. The next refresh of a copy reloads these rows. Touches join the caller's unit of work and are saved with it; types
/// without a client entity set are ignored. The library's tags, ratings, comments and reactions report themselves.
/// </summary>
public interface IClientEntityChangeLog
{
    void Touch(Type entityType, IEnumerable<Guid> ids);

    void Touch<TEntity>(IEnumerable<Guid> ids)
        where TEntity : class => Touch(typeof(TEntity), ids);

    void Touch<TEntity>(params Guid[] ids)
        where TEntity : class => Touch(typeof(TEntity), ids);
}

/// <summary>A row of <c>cw.ClientEntityTouches</c>: <see cref="EntityId"/> of <see cref="EntityType"/> changed at <see cref="TouchedAt"/>.</summary>
public sealed class ClientEntityTouch : Entity
{
    /// <summary>The tenant of the caller who touched it; the feed reads its own and those without one, then keeps only the rows the reader sees.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The full name of the entity's CLR type.</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public DateTimeOffset TouchedAt { get; set; }
}

internal sealed class ClientEntityChangeLog(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock, IOptions<ClientEntityOptions> options) : IClientEntityChangeLog
{
    public void Touch(Type entityType, IEnumerable<Guid> ids)
    {
        if (options.Value.Resolve(entityType) is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        db.AddRange(ids.Distinct().Select(id => new ClientEntityTouch { TenantId = currentUser.TenantId, EntityType = entityType.FullName!, EntityId = id, TouchedAt = now }));
    }

    /// <summary>The ids of <paramref name="entityType"/> touched after <paramref name="from"/>, at most <paramref name="take"/>.</summary>
    public Task<List<Guid>> SinceAsync(Type entityType, DateTimeOffset from, int take, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        return db.Set<ClientEntityTouch>().AsNoTracking()
            .Where(t => t.EntityType == entityType.FullName && t.TouchedAt > from && (t.TenantId == null || t.TenantId == tenantId))
            .Select(t => t.EntityId).Distinct().Take(take).ToListAsync(cancellationToken);
    }
}

internal sealed class ClientEntityTouchModel : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<ClientEntityTouch>(touch =>
    {
        touch.ToTable("ClientEntityTouches", "cw");
        touch.Property(t => t.EntityType).HasMaxLength(300);
        touch.HasIndex(t => new { t.EntityType, t.TouchedAt });
    });
}

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
