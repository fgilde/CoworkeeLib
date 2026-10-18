using System.Reflection;
using Coworkee.Application.Retention;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention.Targets;

/// <summary>Every soft deleted type of the model, one after the other; a type that cannot go (a foreign key) is reported and the next one is tried.</summary>
internal sealed class SoftDeletedRetention(CoworkeeDbContext db, IEnumerable<RetentionOwnedType> owned) : IRetentionTarget
{
    private static readonly MethodInfo CountMethod = typeof(SoftDeletedRetention).GetMethod(nameof(CountTypeAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo DeleteMethod = typeof(SoftDeletedRetention).GetMethod(nameof(DeleteTypeAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public string Key => "soft-deleted";

    public string Title => "Deleted records";

    public string? Description => "Records that were deleted softly and could still be restored.";

    public int? DefaultDays => null;

    public async Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var type in Types())
        {
            total += await (Task<long>)CountMethod.MakeGenericMethod(type).Invoke(this, [before, cancellationToken])!;
        }

        return total;
    }

    public async Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        long total = 0;
        var failed = new List<string>();
        foreach (var type in Types())
        {
            try
            {
                total += await (Task<long>)DeleteMethod.MakeGenericMethod(type).Invoke(this, [before, cancellationToken])!;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed.Add($"{type.Name}: {exception.GetBaseException().Message}");
            }
        }

        return failed.Count == 0 ? total : throw new RetentionIncompleteException(total, string.Join(Environment.NewLine, failed));
    }

    private IEnumerable<Type> Types()
    {
        var skipped = owned.Select(o => o.Type).ToHashSet();
        return db.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && !t.IsOwned() && typeof(ISoftDelete).IsAssignableFrom(t.ClrType) && !skipped.Contains(t.ClrType))
            .Select(t => t.ClrType)
            .Distinct()
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    private Task<long> CountTypeAsync<T>(DateTimeOffset before, CancellationToken cancellationToken) where T : class, ISoftDelete =>
        Older<T>(before).LongCountAsync(cancellationToken);

    private Task<long> DeleteTypeAsync<T>(DateTimeOffset before, CancellationToken cancellationToken) where T : class, ISoftDelete =>
        Older<T>(before).OrderBy(e => e.DeletedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<T> Older<T>(DateTimeOffset before) where T : class, ISoftDelete =>
        db.Set<T>().IgnoreQueryFilters().Where(e => e.IsDeleted && e.DeletedAt < before);
}
