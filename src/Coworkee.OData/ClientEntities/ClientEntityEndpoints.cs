using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.ClientEntities;

/// <summary>
/// The sync feed of client entities. Rows travel through the OData set itself (same shape, expand, hidden properties and row filters);
/// these endpoints only say which sets may be mirrored, what changed since a version and which ids are still visible.
/// </summary>
internal static class ClientEntityEndpoints
{

    public static void MapClientEntityEndpoints(this IEndpointRouteBuilder app)
    {
        var sync = app.MapCoworkeeApi("/api/v1/client-entities").WithTags("Data").RequireAuthorization();
        sync.MapGet("/", ManifestAsync);
        // with a skip the changed ids come in pages (More); without, more than ten pages of them ask for a reset as before
        sync.MapGet("/{entitySet}/changes", (string entitySet, DateTimeOffset? since, int? skip, HttpContext http) =>
            DispatchAsync(entitySet, http, nameof(ChangesAsync), new Feed(since, skip is { } s ? Math.Max(0, s) : null)));
        sync.MapGet("/{entitySet}/ids", (string entitySet, HttpContext http) => DispatchAsync(entitySet, http, nameof(IdsAsync), new Feed(null, null)));
    }

    private static async Task<IResult> ManifestAsync(HttpContext http, ICurrentUser user)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<ClientEntityOptions>>().Value;
        var maxTop = services.GetRequiredService<IOptions<CoworkeeODataOptions>>().Value.MaxTop;
        var sets = new List<ClientEntitySetInfo>();
        // the copy tracks rows by their Guid Id: sets keyed otherwise stay on the server
        foreach (var entity in services.GetRequiredService<ODataEntityRegistry>().Entities.Where(e => e.HasGuidId))
        {
            if (options.Resolve(entity.EntityType) is { } settings && await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, http.RequestAborted))
            {
                // a [Realtime] entity or a watched set (RealtimeEntityRegistration): the copy updates when the server reports a change
                var realtime = entity.EntityType.GetCustomAttribute<RealtimeAttribute>(inherit: false) is not null
                    || services.GetServices<Coworkee.Infrastructure.Persistence.RealtimeEntityRegistration>().Any(r => r.EntityType == entity.EntityType)
                    ? RealtimeTopics.Type(entity.EntityType.Name)
                    : null;
                sets.Add(new ClientEntitySetInfo(entity.EntitySet, Math.Min(settings.BatchSize, maxTop), settings.MaxRows, settings.RefreshInterval, realtime,
                    string.IsNullOrWhiteSpace(settings.Expand) ? null : settings.Expand, Math.Max(1, settings.ParallelPages)));
            }
        }

        return Results.Ok(new ClientEntityManifest(await PartitionAsync(services, user, http.RequestAborted), sets, Hash(Owner(user))));
    }

    private static string Owner(ICurrentUser user) => $"{user.UserId}|{user.TenantId}|{user.ClientId}";

    /// <summary>Who the request reads for: user, tenant, client and every <see cref="IClientEntityPartitionPart"/> (the group selection), hashed.</summary>
    internal static Task<string> PartitionAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        PartitionAsync(services, services.GetRequiredService<ICurrentUser>(), cancellationToken);

    private static async Task<string> PartitionAsync(IServiceProvider services, ICurrentUser user, CancellationToken cancellationToken)
    {
        var key = new StringBuilder(Owner(user));
        foreach (var part in services.GetServices<IClientEntityPartitionPart>())
        {
            if (await part.GetAsync(cancellationToken) is { } value)
            {
                key.Append('|').Append(value);
            }
        }

        return Hash(key.ToString());
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();

    private static async Task<IResult> DispatchAsync(string entitySet, HttpContext http, string method, Feed feed)
    {
        var services = http.RequestServices;
        var entity = services.GetRequiredService<ODataEntityRegistry>().Entities
            .FirstOrDefault(e => string.Equals(e.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase));
        if (entity is not { HasGuidId: true } || services.GetRequiredService<IOptions<ClientEntityOptions>>().Value.Resolve(entity.EntityType) is not { } settings)
        {
            return Results.NotFound();
        }

        if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, http.RequestAborted))
        {
            return Results.Forbid();
        }

        return await (Task<IResult>)typeof(ClientEntityEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(entity.EntityType)
            .Invoke(null, [services, settings, feed, http.RequestAborted])!;
    }

    private static async Task<IResult> ChangesAsync<TEntity>(IServiceProvider services, ClientEntitySettings settings, Feed feed, CancellationToken cancellationToken)
        where TEntity : class
    {
        var version = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var visible = await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken);
        var count = await visible.LongCountAsync(cancellationToken);
        // the client compares it with the partition of its copy: another user, tenant or group selection meanwhile drops the copy
        var partition = await PartitionAsync(services, services.GetRequiredService<ICurrentUser>(), cancellationToken);
        if (feed.Since is not { } since)
        {
            return Results.Ok(new ClientEntityChanges(version, count, [], [], Reset: false, Partition: partition));
        }

        // touches older than the change log's lifetime are gone: such a copy cannot tell which rows they were
        var log = services.GetRequiredService<IOptions<ClientEntityOptions>>().Value.ChangeLogLifetime;
        if (since < version - log)
        {
            return Results.Ok(new ClientEntityChanges(version, count, [], [], Reset: true, Partition: partition));
        }

        var from = since - settings.Overlap;
        var changedSince = settings.ChangedAt.Count > 0 ? ChangedAfter<TEntity>(services, settings.ChangedAt, from)
            : typeof(IAuditable).IsAssignableFrom(typeof(TEntity)) ? ChangedAfter<TEntity>(services, [nameof(IAuditable.CreatedAt), nameof(IAuditable.ModifiedAt)], from)
            : null;
        if (changedSince is null)
        {
            return Results.Ok(new ClientEntityChanges(version, count, [], [], Reset: true, Partition: partition));
        }

        // pages in the order of the ids: rows changing meanwhile only join the set (they changed after "from" too), so a skip misses none
        var limit = settings.BatchSize * 10;
        // rows reported through IClientEntityChangeLog (a tag put on, no ChangedAt moved); of them only the visible ones come out
        var touched = await services.GetRequiredService<ClientEntityChangeLog>().SinceAsync(typeof(TEntity), from, limit + 1, cancellationToken);
        if (touched.Count > limit)
        {
            return Results.Ok(new ClientEntityChanges(version, count, [], [], Reset: true, Partition: partition));
        }

        var ids = visible.Where(changedSince).Select(e => EF.Property<Guid>(e, nameof(Entity.Id)));
        if (touched.Count > 0)
        {
            ids = ids.Union(visible.Where(e => EF.Parameter(touched).Contains(EF.Property<Guid>(e, nameof(Entity.Id)))).Select(e => EF.Property<Guid>(e, nameof(Entity.Id))));
        }

        var changed = await ids
            .OrderBy(id => id)
            .Skip(feed.Skip ?? 0)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var more = changed.Count > limit;
        if (more && feed.Skip is null)
        {
            return Results.Ok(new ClientEntityChanges(version, count, [], [], Reset: true, Partition: partition));
        }

        if (more)
        {
            changed.RemoveAt(limit);
        }

        // the deleted ids come with the first page only
        List<Guid> deleted = [];
        if (feed.Skip is null or 0 && typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            deleted = await (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken, withDeleted: true))
                .Where(e => EF.Property<bool>(e, nameof(ISoftDelete.IsDeleted)) && EF.Property<DateTimeOffset?>(e, nameof(ISoftDelete.DeletedAt)) > from)
                .Select(e => EF.Property<Guid>(e, nameof(Entity.Id)))
                .ToListAsync(cancellationToken);
        }

        return Results.Ok(new ClientEntityChanges(version, count, changed, deleted, Reset: false, more, partition));
    }

    /// <summary>e => e.A > from || e.B > from ..., with <paramref name="from"/> as a query parameter in the column's type.</summary>
    private static Expression<Func<TEntity, bool>> ChangedAfter<TEntity>(IServiceProvider services, IEnumerable<string> properties, DateTimeOffset from)
    {
        var entity = services.DbContextFor(typeof(TEntity)).Model.FindEntityType(typeof(TEntity))!;
        var e = Expression.Parameter(typeof(TEntity), "e");
        Expression? any = null;
        foreach (var name in properties)
        {
            var property = entity.FindProperty(name) ?? throw new InvalidOperationException($"{typeof(TEntity).Name} has no property {name} for ChangedAt.");
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            Expression bound = type == typeof(DateTimeOffset) ? Expression.Field(Expression.Constant(new StrongBox<DateTimeOffset>(from)), "Value")
                : type == typeof(DateTime) ? Expression.Field(Expression.Constant(new StrongBox<DateTime>(Utc(from, property.GetColumnType()))), "Value")
                : throw new InvalidOperationException($"{typeof(TEntity).Name}.{name} is no DateTime or DateTimeOffset.");
            var column = Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType], e, Expression.Constant(name));
            var after = Expression.GreaterThan(column, Expression.Convert(bound, property.ClrType));
            any = any is null ? after : Expression.OrElse(any, after);
        }

        return Expression.Lambda<Func<TEntity, bool>>(any!, e);
    }

    // Npgsql takes only unspecified DateTimes for a timestamp without time zone, other providers either kind
    private static DateTime Utc(DateTimeOffset value, string columnType) =>
        DateTime.SpecifyKind(value.UtcDateTime, columnType.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase) && !columnType.Contains("with time zone", StringComparison.OrdinalIgnoreCase)
            ? DateTimeKind.Unspecified
            : DateTimeKind.Utc);

    private static async Task<IResult> IdsAsync<TEntity>(IServiceProvider services, ClientEntitySettings settings, Feed feed, CancellationToken cancellationToken)
        where TEntity : class
    {
        var ids = await (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken))
            .Select(e => EF.Property<Guid>(e, nameof(Entity.Id)))
            .Take(settings.MaxRows + 1)
            .ToListAsync(cancellationToken);
        return Results.Ok(ids);
    }

    private sealed record Feed(DateTimeOffset? Since, int? Skip);
}
