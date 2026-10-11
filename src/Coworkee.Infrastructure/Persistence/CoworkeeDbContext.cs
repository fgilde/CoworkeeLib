using System.Reflection;
using Coworkee.Application;
using Coworkee.Core;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Coworkee.Infrastructure.Persistence;

public abstract class CoworkeeDbContext(DbContextOptions options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : DbContext(options), IUnitOfWork
{
    public const string SoftDeleteFilter = "SoftDelete";
    public const string TenantFilter = "Tenant";

    public const string SqlServerCollation = "Latin1_General_100_CS_AS";

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
            if (typeof(IHasConcurrencyToken).IsAssignableFrom(entityType.ClrType))
            {
                // Postgres maps it to xmin; elsewhere it is a plain column the AuditablePropertiesInterceptor counts up
                var version = modelBuilder.Entity(entityType.ClrType).Property(nameof(IHasConcurrencyToken.Version));
                _ = Database.IsNpgsql() ? version.IsRowVersion() : version.IsConcurrencyToken();
            }
        }

        if (!Database.IsNpgsql())
        {
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()).Where(p => p.GetColumnType() == "jsonb"))
            {
                property.SetColumnType(null);
            }
        }

        if (Database.IsSqlServer())
        {
            // compare text case-sensitively like Postgres: unique keys such as "System" and "system" are different rows
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()).Where(p => p.ClrType == typeof(string) && p.GetCollation() is null))
            {
                property.SetCollation(SqlServerCollation);
            }

            // ponytail: SQL Server refuses cascades on self references (comment replies); EF deletes the loaded children, a bulk delete of a parent with children fails
            foreach (var key in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys())
                         .Where(k => k.PrincipalEntityType == k.DeclaringEntityType && k.DeleteBehavior == DeleteBehavior.Cascade))
            {
                key.DeleteBehavior = DeleteBehavior.ClientCascade;
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.IsSqlite())
        {
            // SQLite cannot compare or sort these as stored by default
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            // ponytail: decimals as double on SQLite, a development database; exact money math needs Postgres or SQL Server
            configurationBuilder.Properties<decimal>().HaveConversion<double>();
            configurationBuilder.Properties<decimal?>().HaveConversion<double>();
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
