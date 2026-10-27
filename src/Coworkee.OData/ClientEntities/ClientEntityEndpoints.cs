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
    // ponytail: rows saved within this window before a version are sent again, covering clock skew between servers and late commits.
    private static readonly TimeSpan Overlap = TimeSpan.FromSeconds(5);

    public static void MapClientEntityEndpoints(this IEndpointRouteBuilder app)
    {
        var sync = app.MapCoworkeeApi("/api/v1/client-entities").WithTags("Data").RequireAuthorization();
        sync.MapGet("/", ManifestAsync);
        sync.MapGet("/{entitySet}/changes", (string entitySet, DateTimeOffset? since, HttpContext http) =>
            DispatchAsync(entitySet, http, nameof(ChangesAsync), since));
        sync.MapGet("/{entitySet}/ids", (string entitySet, HttpContext http) => DispatchAsync(entitySet, http, nameof(IdsAsync), null));
    }

    private static async Task<IResult> ManifestAsync(HttpContext http, ICurrentUser user)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<ClientEntityOptions>>().Value;
        var maxTop = services.GetRequiredService<IOptions<CoworkeeODataOptions>>().Value.MaxTop;
        var sets = new List<ClientEntitySetInfo>();
        foreach (var entity in services.GetRequiredService<ODataEntityRegistry>().Entities)
        {
            if (options.Resolve(entity.EntityType) is { } settings && await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, http.RequestAborted))
            {
                var realtime = entity.EntityType.GetCustomAttribute<RealtimeAttribute>(inherit: false) is null ? null : RealtimeTopics.Type(entity.EntityType.Name);
                sets.Add(new ClientEntitySetInfo(entity.EntitySet, Math.Min(settings.BatchSize, maxTop), settings.MaxRows, settings.RefreshInterval, realtime));
            }
        }

        return Results.Ok(new ClientEntityManifest(await PartitionAsync(services, user, http.RequestAborted), sets));
    }

    private static async Task<string> PartitionAsync(IServiceProvider services, ICurrentUser user, CancellationToken cancellationToken)
    {
        var key = new StringBuilder($"{user.UserId}|{user.TenantId}|{user.ClientId}");
        foreach (var part in services.GetServices<IClientEntityPartitionPart>())
        {
            if (await part.GetAsync(cancellationToken) is { } value)
            {
                key.Append('|').Append(value);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())))[..16].ToLowerInvariant();
    }

    private static async Task<IResult> DispatchAsync(string entitySet, HttpContext http, string method, DateTimeOffset? since)
    {
        var services = http.RequestServices;
        var entity = services.GetRequiredService<ODataEntityRegistry>().Entities
            .FirstOrDefault(e => string.Equals(e.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase));
        if (entity is null || services.GetRequiredService<IOptions<ClientEntityOptions>>().Value.Resolve(entity.EntityType) is not { } settings)
        {
            return Results.NotFound();
        }

        if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, http.RequestAborted))
        {
            return Results.Forbid();
        }

        return await (Task<IResult>)typeof(ClientEntityEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(entity.EntityType)
            .Invoke(null, [services, settings, since, http.RequestAborted])!;
    }

    private static async Task<IResult> ChangesAsync<TEntity>(IServiceProvider services, ClientEntitySettings settings, DateTimeOffset? since, CancellationToken cancellationToken)
        where TEntity : class
    {
        var version = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var visible = await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken);
        var count = await visible.LongCountAsync(cancellationToken);
        if (since is null)
        {
            return Results.Ok(new ClientEntityChanges(version, count, [], [], Reset: false));
        }

        var from = since.Value - Overlap;
        var changedSince = settings.ChangedAt.Count > 0 ? ChangedAfter<TEntity>(settings.ChangedAt, from)
            : typeof(IAuditable).IsAssignableFrom(typeof(TEntity)) ? ChangedAfter<TEntity>([nameof(IAuditable.CreatedAt), nameof(IAuditable.ModifiedAt)], from)
            : null;
        if (changedSince is null)
        {
            return Results.Ok(new ClientEntityChanges(version, count, [], [], Reset: true));
        }

        var limit = settings.BatchSize * 10;
        var changed = await visible
            .Where(changedSince)
            .Select(e => EF.Property<Guid>(e, nameof(Entity.Id)))
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        if (changed.Count > limit)
        {
            return Results.Ok(new ClientEntityChanges(version, count, [], [], Reset: true));
        }

        List<Guid> deleted = [];
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            deleted = await (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken, withDeleted: true))
                .Where(e => EF.Property<bool>(e, nameof(ISoftDelete.IsDeleted)) && EF.Property<DateTimeOffset?>(e, nameof(ISoftDelete.DeletedAt)) > from)
                .Select(e => EF.Property<Guid>(e, nameof(Entity.Id)))
                .ToListAsync(cancellationToken);
        }

        return Results.Ok(new ClientEntityChanges(version, count, changed, deleted, Reset: false));
    }

    /// <summary>e => e.A > from || e.B > from ..., with <paramref name="from"/> as a query parameter in the column's type.</summary>
    private static Expression<Func<TEntity, bool>> ChangedAfter<TEntity>(IEnumerable<string> properties, DateTimeOffset from)
    {
        var e = Expression.Parameter(typeof(TEntity), "e");
        Expression? any = null;
        foreach (var name in properties)
        {
            var column = Expression.Property(e, name);
            var type = Nullable.GetUnderlyingType(column.Type) ?? column.Type;
            Expression bound = type == typeof(DateTimeOffset) ? Expression.Field(Expression.Constant(new StrongBox<DateTimeOffset>(from)), "Value")
                : type == typeof(DateTime) ? Expression.Field(Expression.Constant(new StrongBox<DateTime>(from.UtcDateTime)), "Value")
                : throw new InvalidOperationException($"{typeof(TEntity).Name}.{name} is no DateTime or DateTimeOffset.");
            var after = Expression.GreaterThan(column, Expression.Convert(bound, column.Type));
            any = any is null ? after : Expression.OrElse(any, after);
        }

        return Expression.Lambda<Func<TEntity, bool>>(any!, e);
    }

    private static async Task<IResult> IdsAsync<TEntity>(IServiceProvider services, ClientEntitySettings settings, DateTimeOffset? since, CancellationToken cancellationToken)
        where TEntity : class
    {
        var ids = await (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken))
            .Select(e => EF.Property<Guid>(e, nameof(Entity.Id)))
            .Take(settings.MaxRows + 1)
            .ToListAsync(cancellationToken);
        return Results.Ok(ids);
    }
}
