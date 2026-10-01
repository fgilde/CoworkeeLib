using System.Reflection;
using Coworkee.Application;
using Coworkee.Core;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Persistence;

public abstract class CoworkeeDbContext(DbContextOptions options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : DbContext(options), IUnitOfWork
{
    public const string SoftDeleteFilter = "SoftDelete";
    public const string TenantFilter = "Tenant";

    private static readonly MethodInfo ApplyFiltersMethod =
        typeof(CoworkeeDbContext).GetMethod(nameof(ApplyQueryFilters), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public Guid? CurrentTenantId => currentUser.TenantId;

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The entity was changed by someone else.", exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var contributor in contributors)
        {
            contributor.Apply(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null && !t.IsOwned()).ToArray())
        {
            ApplyFiltersMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            if (typeof(IHasConcurrencyToken).IsAssignableFrom(entityType.ClrType) && Database.IsNpgsql())
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(IHasConcurrencyToken.Version)).IsRowVersion();
            }
        }
    }

    private void ApplyQueryFilters<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class
    {
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(SoftDeleteFilter, e => !((ISoftDelete)e).IsDeleted);
        }

        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(TenantFilter, e => ((IMultiTenant)e).TenantId == CurrentTenantId);
        }
    }
}
