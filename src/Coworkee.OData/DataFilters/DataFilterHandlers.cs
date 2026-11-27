using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.DataFilters;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;

namespace Coworkee.OData.DataFilters;

internal sealed class DataFilterHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, ODataEntityRegistry registry, IServiceProvider services,
    IOptions<CoworkeeODataOptions> options)
    : IHandler<SaveDataFilterCommand, Result<DataFilterDto>>,
      IHandler<DeleteDataFilterCommand, Result>,
      IHandler<GetDataFiltersQuery, Result<IReadOnlyList<DataFilterDto>>>,
      IHandler<SaveDataFilterLinkCommand, Result<DataFilterLinkDto>>,
      IHandler<DeleteDataFilterLinkCommand, Result>,
      IHandler<GetDataFilterLinksQuery, Result<IReadOnlyList<DataFilterLinkDto>>>,
      IHandler<GetDataFilterFieldsQuery, Result<IReadOnlyList<DataFilterFieldDto>>>,
      IHandler<GetDataFilterSetsQuery, Result<IReadOnlyList<string>>>,
      IHandler<PreviewDataFilterQuery, Result<DataFilterPreviewDto>>,
      IHandler<GetDataFilterOverviewQuery, Result<DataFilterOverviewDto>>
{
    private static readonly Error Forbidden = Error.Forbidden("data_filters.forbidden", "You may not manage data filters of this layer.");

    private static readonly Error NotFound = Error.NotFound("data_filters.not_found", "There is no such data filter.");

    private static readonly DataFilterLayer[] Layers = [DataFilterLayer.System, DataFilterLayer.Tenant, DataFilterLayer.Group, DataFilterLayer.Role];

    private static string PermissionOf(DataFilterLayer layer) => layer switch
    {
        DataFilterLayer.System => DataFilterPermissions.ManageSystem,
        DataFilterLayer.Tenant => DataFilterPermissions.ManageTenant,
        DataFilterLayer.Group => DataFilterPermissions.ManageGroups,
        _ => DataFilterPermissions.ManageRoles,
    };

    // System filters and links apply to every organisation: only the system organisation writes them
    private async Task<bool> MayManageAsync(DataFilterLayer layer, CancellationToken cancellationToken) =>
        await permissions.IsGrantedAsync(PermissionOf(layer), cancellationToken)
        && (layer != DataFilterLayer.System || await IsSystemTenantAsync(cancellationToken));

    private async Task<bool> IsSystemTenantAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);

    private async Task<List<DataFilterLayer>> ManageableAsync(CancellationToken cancellationToken)
    {
        var layers = new List<DataFilterLayer>();
        foreach (var layer in Layers)
        {
            if (await MayManageAsync(layer, cancellationToken))
            {
                layers.Add(layer);
            }
        }

        return layers;
    }

    public async Task<Result<DataFilterDto>> HandleAsync(SaveDataFilterCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (!await MayManageAsync(request.Layer, cancellationToken))
        {
            return Forbidden;
        }

        var (entity, filterOption, error) = await ParseDraftAsync(request.EntitySet, request.Query, cancellationToken);
        if (entity is null)
        {
            return error!;
        }

        // without an organisation a non-System filter would belong to nobody
        var tenantId = request.Layer == DataFilterLayer.System ? null : currentUser.TenantId;
        if (request.Layer != DataFilterLayer.System && tenantId is null)
        {
            return Forbidden;
        }

        var filter = await db.Set<DataFilter>().FirstOrDefaultAsync(
            f => f.TenantId == tenantId && f.EntitySet == entity.EntitySet && f.Layer == request.Layer && f.LayerKey == request.LayerKey, cancellationToken);
        if (filter is null)
        {
            filter = new DataFilter { TenantId = tenantId, EntitySet = entity.EntitySet, Layer = request.Layer, LayerKey = request.LayerKey };
            db.Add(filter);
        }

        (filter.Query, filter.AppliesToDetails) = (request.Query, request.AppliesToDetails);
        filter.Version++;
        return ToDto(filter, null);
    }

    // what a draft filter must satisfy before it is saved or tried
    private async Task<(ODataEntityRegistration? Entity, FilterQueryOption? Filter, Error? Error)> ParseDraftAsync(string entitySet, string query, CancellationToken cancellationToken)
    {
        if (registry.Find(entitySet) is not { } entity)
        {
            return (null, null, Error.Validation("entitySet", $"There is no entity set '{entitySet}'."));
        }

        var (filterOption, problem) = DataFilterQueries.Parse(services, entity, query);
        if (problem is not null)
        {
            return (null, null, Error.Validation("query", problem));
        }

        // a filter is no way to probe properties the editing administrator may not read
        if (filterOption is not null
            && ODataQueryGuard.Denies(services, entity.EntityType, filterOption.RawValue, await ODataEntityAccess.DeniedByTypeAsync(services, cancellationToken)))
        {
            return (null, null, Error.Validation("query", "The filter uses a property you may not read."));
        }

        return (entity, filterOption, null);
    }

    public async Task<Result<DataFilterPreviewDto>> HandleAsync(PreviewDataFilterQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        if (!await MayManageAsync(request.Layer, cancellationToken))
        {
            return Forbidden;
        }

        var (entity, filter, error) = await ParseDraftAsync(request.EntitySet, request.Query, cancellationToken);
        if (entity is null)
        {
            return error!;
        }

        // the counts reveal rows: only for those who may read the set
        if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken))
        {
            return Forbidden;
        }

        // the rows the editor sees under every filter that binds him but the edited one (its stored version; a new draft has none), in all his groups:
        // the other layers keep hiding what they hide, so the counts never reveal rows he may not see
        var tenantId = request.Layer == DataFilterLayer.System ? null : currentUser.TenantId;
        var edited = await db.Set<DataFilter>().AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.EntitySet == entity.EntitySet && f.Layer == request.Layer && f.LayerKey == request.LayerKey)
            .Select(f => (Guid?)f.Id).FirstOrDefaultAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.DataFilterPreviewTimeout);
        try
        {
            using var unnarrowed = GroupScope.Unnarrowed();
            using var except = DataFilterScope.Except(edited);
            return await (Task<DataFilterPreviewDto>)CountMethod.MakeGenericMethod(entity.EntityType).Invoke(this, [filter, timeout.Token])!;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new DataFilterPreviewDto(null, null, true);
        }
    }

    private static readonly System.Reflection.MethodInfo CountMethod = typeof(DataFilterHandlers).GetMethod(nameof(CountAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    // the set as the editor sees it (the app's own row filters and the other data filters), then what passes the draft
    private async Task<DataFilterPreviewDto> CountAsync<TEntity>(FilterQueryOption? filter, CancellationToken cancellationToken)
        where TEntity : class
    {
        var visible = await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken);
        var total = await visible.LongCountAsync(cancellationToken);
        var passing = filter is null ? total : await ((IQueryable<TEntity>)filter.ApplyTo(visible, DataFilterQueries.ApplySettings())).LongCountAsync(cancellationToken);
        return new DataFilterPreviewDto(passing, total, false);
    }

    public async Task<Result<DataFilterOverviewDto>> HandleAsync(GetDataFilterOverviewQuery query, CancellationToken cancellationToken)
    {
        var layer = await IsSystemTenantAsync(cancellationToken) ? DataFilterLayer.System : DataFilterLayer.Tenant;
        if (!await MayManageAsync(layer, cancellationToken))
        {
            return Forbidden;
        }

        var tenantId = layer == DataFilterLayer.System ? null : currentUser.TenantId;
        var counts = await db.Set<DataFilter>().AsNoTracking().Where(f => f.Layer == layer && f.TenantId == tenantId)
            .GroupBy(f => f.EntitySet).Select(g => new { EntitySet = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var linked = layer == DataFilterLayer.System
            ? (await db.Set<DataFilterLink>().AsNoTracking().Select(l => l.EntitySet).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var sets = registry.Entities.Select(e => e.EntitySet).Order(StringComparer.OrdinalIgnoreCase)
            .Select(set => new DataFilterSetStateDto(set, counts.Where(c => string.Equals(c.EntitySet, set, StringComparison.OrdinalIgnoreCase)).Sum(c => c.Count), linked.Contains(set)))
            .ToList();
        var name = currentUser.TenantId is { } own && services.GetService<ITenantDirectory>() is { } tenants ? await tenants.GetNameAsync(own, cancellationToken) : null;
        return new DataFilterOverviewDto(layer, name, sets);
    }

    public async Task<Result> HandleAsync(DeleteDataFilterCommand command, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        if (await db.Set<DataFilter>().FirstOrDefaultAsync(f => f.Id == command.Id && (f.TenantId == tenantId || f.TenantId == null), cancellationToken) is not { } filter)
        {
            return NotFound;
        }

        if (!await MayManageAsync(filter.Layer, cancellationToken))
        {
            return Forbidden;
        }

        db.Remove(filter);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<DataFilterDto>>> HandleAsync(GetDataFiltersQuery query, CancellationToken cancellationToken)
    {
        var layers = await ManageableAsync(cancellationToken);
        if (layers.Count == 0)
        {
            return Forbidden;
        }

        var tenantId = currentUser.TenantId;
        var rows = db.Set<DataFilter>().AsNoTracking().Where(f => layers.Contains(f.Layer) && (f.TenantId == tenantId || f.TenantId == null && f.Layer == DataFilterLayer.System));
        if (query.EntitySet is { } set)
        {
            rows = rows.Where(f => f.EntitySet == set);
        }

        if (query.Layer is { } layer)
        {
            rows = rows.Where(f => f.Layer == layer);
        }

        if (query.LayerKey is { } key)
        {
            rows = rows.Where(f => f.LayerKey == key);
        }

        return (await rows.OrderBy(f => f.EntitySet).ThenBy(f => f.Layer).ToListAsync(cancellationToken))
            .Select(f => ToDto(f, registry.Find(f.EntitySet) is { } entity ? DataFilterQueries.Problem(services, entity, f.Query) : $"There is no entity set '{f.EntitySet}'."))
            .ToList();
    }

    public async Task<Result<DataFilterLinkDto>> HandleAsync(SaveDataFilterLinkCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (!await MayManageAsync(DataFilterLayer.System, cancellationToken))
        {
            return Forbidden;
        }

        if (registry.Find(request.EntitySet) is not { } entity || registry.Find(request.TargetSet) is not { } target)
        {
            return Error.Validation("entitySet", "A link joins two existing entity sets.");
        }

        if (entity == target)
        {
            return Error.Validation("targetSet", "A set cannot be linked to itself.");
        }

        // the link narrows by a join: both sets must live in one DbContext
        if (services.DbContextFor(entity.EntityType).GetType() != services.DbContextFor(target.EntityType).GetType())
        {
            return Error.Validation("targetSet", "Linked sets must belong to the same database context.");
        }

        var model = services.GetRequiredService<IEdmModel>();
        if (model.EntityContainer.FindEntitySet(entity.EntitySet)?.EntityType.FindProperty(request.Property) is not IEdmStructuralProperty property)
        {
            return Error.Validation("property", $"{entity.EntitySet} has no property '{request.Property}'.");
        }

        // the link joins on the target's key: the property must hold one
        var key = services.DbContextFor(target.EntityType).Model.FindEntityType(target.EntityType)?.FindPrimaryKey()?.Properties is [{ } single] ? single.ClrType : null;
        var own = DataFilterProperties.Find(entity.EntityType, property.Name)?.PropertyType;
        if (key is null || own is null || (Nullable.GetUnderlyingType(own) ?? own) != (Nullable.GetUnderlyingType(key) ?? key))
        {
            return Error.Validation("property", $"{entity.EntitySet}.{property.Name} cannot hold the key of {target.EntitySet}.");
        }

        // a chain that comes back to this set would hide every row of every set in it
        var links = await db.Set<DataFilterLink>().ToListAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entity.EntitySet };
        for (var next = target.EntitySet; next is not null; next = links.FirstOrDefault(l => string.Equals(l.EntitySet, next, StringComparison.OrdinalIgnoreCase))?.TargetSet)
        {
            if (!seen.Add(next))
            {
                return Error.Validation("targetSet", $"Linking {entity.EntitySet} to {target.EntitySet} makes a chain of links that comes back to {next}.");
            }
        }

        var link = links.FirstOrDefault(l => string.Equals(l.EntitySet, entity.EntitySet, StringComparison.OrdinalIgnoreCase));
        if (link is null)
        {
            link = new DataFilterLink { EntitySet = entity.EntitySet };
            db.Add(link);
        }

        (link.Property, link.TargetSet) = (property.Name, target.EntitySet);
        return new DataFilterLinkDto(link.Id, link.EntitySet, link.Property, link.TargetSet);
    }

    public async Task<Result> HandleAsync(DeleteDataFilterLinkCommand command, CancellationToken cancellationToken)
    {
        if (!await MayManageAsync(DataFilterLayer.System, cancellationToken))
        {
            return Forbidden;
        }

        if (await db.Set<DataFilterLink>().FirstOrDefaultAsync(l => l.Id == command.Id, cancellationToken) is not { } link)
        {
            return NotFound;
        }

        db.Remove(link);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<DataFilterLinkDto>>> HandleAsync(GetDataFilterLinksQuery query, CancellationToken cancellationToken) =>
        (await ManageableAsync(cancellationToken)).Count == 0
            ? Forbidden
            : (await db.Set<DataFilterLink>().AsNoTracking().OrderBy(l => l.EntitySet).ToListAsync(cancellationToken))
                .Select(l => new DataFilterLinkDto(l.Id, l.EntitySet, l.Property, l.TargetSet)).ToList();

    public async Task<Result<IReadOnlyList<string>>> HandleAsync(GetDataFilterSetsQuery query, CancellationToken cancellationToken) =>
        (await ManageableAsync(cancellationToken)).Count == 0
            ? Forbidden
            : registry.Entities.Select(e => e.EntitySet).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The properties a filter on the set may name, with those only some users see flagged.</summary>
    public async Task<Result<IReadOnlyList<DataFilterFieldDto>>> HandleAsync(GetDataFilterFieldsQuery query, CancellationToken cancellationToken)
    {
        if ((await ManageableAsync(cancellationToken)).Count == 0)
        {
            return Forbidden;
        }

        if (registry.Find(query.EntitySet) is not { } entity)
        {
            return Error.Validation("entitySet", $"There is no entity set '{query.EntitySet}'.");
        }

        var model = services.GetRequiredService<IEdmModel>();
        var type = model.EntityContainer.FindEntitySet(entity.EntitySet)!.EntityType;
        return Query.EdmQueryFields.For(model, entity.EntitySet)
            .Select(f => new DataFilterFieldDto(f.Name, f.Type, f.Nullable, f.Flags, f.Options, IsProtected(entity, type, f.Name)))
            .ToList();
    }

    // a property of a navigation counts by the registration of the set that shows that type
    private bool IsProtected(ODataEntityRegistration entity, IEdmEntityType type, string name)
    {
        var dot = name.IndexOf('.');
        if (dot < 0)
        {
            return entity.ProtectedProperties.ContainsKey(name) || entity.SharedHidden.Contains(name);
        }

        var target = (type.FindProperty(name[..dot]) as IEdmNavigationProperty)?.ToEntityType().Name;
        var other = registry.Entities.FirstOrDefault(e => e.EntityType.Name == target);
        var property = name[(dot + 1)..];
        return other is not null && (other.ProtectedProperties.ContainsKey(property) || other.SharedHidden.Contains(property));
    }

    private static DataFilterDto ToDto(DataFilter f, string? problem) => new(f.Id, f.EntitySet, f.Layer, f.LayerKey, f.Query, f.AppliesToDetails, problem is null, problem);
}
