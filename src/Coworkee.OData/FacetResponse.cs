using System.Linq.Dynamic.Core;
using System.Reflection;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using Nextended.Core.Facets;
using Nextended.Web.OData;

namespace Coworkee.OData;

/// <summary>
/// The facets of a page as <see cref="FacetGroupDto"/>s. With a <see cref="FacetContextDto"/> a group with a choice counts on the rows of
/// every other choice but its own (disjunctive), so "any of" groups keep their alternatives; "all of" groups count with their choice.
/// </summary>
internal static class FacetResponse
{
    public static async Task<List<FacetGroupDto>> BuildAsync<T>(HttpContext http, IQueryable<T> visible, IQueryable<T> filtered, IReadOnlyList<FacetGroup> facets,
        CancellationToken cancellationToken)
        where T : class
    {
        var services = http.RequestServices;
        var registry = services.GetRequiredService<ODataEntityRegistry>();
        var context = FacetContextDto.Decode(http.Request.Headers[FacetContextDto.Header]);
        var result = new List<FacetGroupDto>(facets.Count);
        var denied = http.Items[ODataEntityAccess.DeniedItem] is Dictionary<string, IReadOnlySet<string>> byType
            ? byType.GetValueOrDefault(typeof(T).FullName!) ?? new HashSet<string>()
            : await ODataEntityAccess.DeniedPropertiesAsync(services, typeof(T), cancellationToken);

        foreach (var facet in facets.Where(f => !denied.Contains(f.Field)))
        {
            if (await NarrowedAsync(visible, facet.ValuePath, http, cancellationToken))
            {
                continue;
            }

            var own = context is not null && context.Groups.ContainsKey(facet.Key) && context.Combines?.GetValueOrDefault(facet.Key) != FacetCombine.All;
            var rows = own ? await FilterAsync(visible, context!.Without(facet.Key), http, cancellationToken) : filtered;
            var group = facet;
            if (own && facet.Type is not (FacetType.Range or FacetType.DateRange))
            {
                // ponytail: one build per chosen group computes every group; build only the asked group when facets get many
                var rebuilt = await services.GetRequiredService<IFacetBuilder>().BuildAsync(rows, cancellationToken);
                group = rebuilt.FirstOrDefault(g => g.Key == facet.Key) ?? facet;
            }

            var dto = await VisibleLabelsAsync(FacetDtos.From(group, typeof(T), registry), group, rows, http, cancellationToken);
            if (dto.ValueKind is FacetValueKind.Number or FacetValueKind.Date && facet.Type is FacetType.Range or FacetType.DateRange)
            {
                var property = typeof(T).GetProperty(facet.Field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                dto = await FacetRanges.AddAsync(dto, rows, property is null ? 12 : registry.FacetBehavior(typeof(T), property)?.Buckets ?? 12, cancellationToken);
            }

            result.Add(dto);
        }

        foreach (var property in services.GetRequiredService<Computed.ODataComputedProperties>().Of(typeof(T)).Where(p => p.Facet is not null && !denied.Contains(p.Name)))
        {
            var own = context is not null && context.Groups.ContainsKey(property.Name);
            result.Add(await ComputedAsync(property, own ? await FilterAsync(visible, context!.Without(property.Name), http, cancellationToken) : filtered, http, cancellationToken));
        }

        return result;
    }

    // ponytail: one count per option; fine for the handful of workflow states, group in the database if options get many
    private static async Task<FacetGroupDto> ComputedAsync<T>(Computed.ODataComputedProperty property, IQueryable<T> rows, HttpContext http, CancellationToken cancellationToken)
        where T : class
    {
        var options = new List<FacetOptionDto>();
        foreach (var option in property.Facet!.Options)
        {
            var count = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(await FilterAsync(rows, option.OData, http, cancellationToken), cancellationToken);
            options.Add(new FacetOptionDto(JsonSerializer.SerializeToElement(option.Value), option.Label, count > 0, count, option.OData)
            {
                Meta = option.Color is null ? null : new Dictionary<string, string> { ["color"] = option.Color },
            });
        }

        return new FacetGroupDto(property.Name, property.Facet.Label, property.Name, FacetKind.CheckboxList, FacetOperator.Or, options, true, int.MaxValue)
        {
            ValuePath = property.Name,
            ValueKind = FacetValueKind.Enum,
            Combine = FacetCombine.Any,
        };
    }

    /// <summary>
    /// Labels read through a navigation (<c>LabelPath = "it.Customer.Name"</c>) name only rows the user may see in the target's set: the builder
    /// read them without its row filters and permission, so they are read again through the navigation scope; a hidden row shows its key.
    /// </summary>
    private static async Task<FacetGroupDto> VisibleLabelsAsync<T>(FacetGroupDto dto, FacetGroup group, IQueryable<T> rows, HttpContext http, CancellationToken cancellationToken)
        where T : class
    {
        var label = group.LabelPath?.Replace('/', '.');
        if (label is null || group.ValuePath is not { } value || dto.Options.Count == 0 || !(label.StartsWith("it.", StringComparison.Ordinal) ? label[3..] : label).Contains('.'))
        {
            return dto;
        }

        var matching = dto.Options.All(o => !string.IsNullOrEmpty(o.OData))
            ? await FilterAsync(rows, string.Join(" or ", dto.Options.Select(o => $"({o.OData})")), http, cancellationToken)
            : rows;
        var pairs = matching.Select($"new ({value.Replace('/', '.')} as Value, {label} as Label)").Distinct();
        var shown = (await (await ScopeOf<T>(http).ApplyAsync(pairs, cancellationToken)).ToDynamicListAsync(cancellationToken))
            .Cast<DynamicClass>()
            .Where(p => p.GetDynamicPropertyValue("Label") is not null)
            .Select(p => p.GetDynamicPropertyValue("Value")?.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return dto with { Options = [.. dto.Options.Select(o => shown.Contains(Text(o.Value)) ? o : o with { Label = Text(o.Value) })] };
    }

    /// <summary>
    /// Whether a facet's value read through a navigation (<c>ValuePath = "it.Customer.Region"</c>) would group over rows the user may not see:
    /// its target has row filters or a permission he lacks, or the value is a member denied to him. Such a group is left out; the key of the
    /// target (<c>Customer/Id</c>, the foreign key's value) and complex members (<c>Address/City</c>) stay.
    /// </summary>
    // ponytail: left out rather than counted over the scoped rows; count through the scope if apps need such facets on filtered targets
    private static async Task<bool> NarrowedAsync<T>(IQueryable<T> rows, string? valuePath, HttpContext http, CancellationToken cancellationToken)
        where T : class
    {
        var path = valuePath?.StartsWith("it/", StringComparison.Ordinal) == true ? valuePath[3..] : valuePath;
        if (path?.Split('/') is not [var first, var second, ..] segments)
        {
            return false;
        }

        var denied = http.Items[ODataEntityAccess.DeniedItem] as Dictionary<string, IReadOnlySet<string>> ?? await ODataEntityAccess.DeniedByTypeAsync(http.RequestServices, cancellationToken);
        if (segments.Length == 2 && ForeignKeyRead<T>(http.RequestServices, first, second, denied))
        {
            return false;
        }

        try
        {
            if (ODataQueryGuard.Denies(http.RequestServices, typeof(T), $"{path} ne null", denied))
            {
                return true;
            }
        }
        catch (Microsoft.OData.ODataException)
        {
            return true;
        }

        var read = rows.Select($"it.{path.Replace('/', '.')}");
        var scope = ScopeOf<T>(http);
        await scope.PrepareAsync(read.Expression, cancellationToken);
        return scope.Narrows(read.Expression);
    }

    // Nav/Key reads the row's own foreign key column: only when that column is mapped on T, readable for the user, and the key is the one it points to
    private static bool ForeignKeyRead<T>(IServiceProvider services, string navigationName, string keyName, IReadOnlyDictionary<string, IReadOnlySet<string>> denied)
    {
        var entity = Coworkee.Infrastructure.Persistence.ExternalDbContextServiceCollectionExtensions.DbContextFor(services, typeof(T)).Model.FindEntityType(typeof(T));
        if (entity?.GetNavigations().FirstOrDefault(n => string.Equals(n.Name, navigationName, StringComparison.OrdinalIgnoreCase)) is not { IsCollection: false, IsOnDependent: true } navigation
            || navigation.ForeignKey.Properties is not [var column] || column.IsShadowProperty()
            || navigation.ForeignKey.PrincipalKey.Properties is not [var key] || !string.Equals(key.Name, keyName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hidden = services.GetRequiredService<ODataEntityRegistry>().Find(typeof(T))?.HiddenProperties ?? [];
        return !hidden.Contains(column.Name) && !(denied.GetValueOrDefault(typeof(T).FullName!)?.Contains(column.Name) ?? false);
    }

    /// <summary>Refuses a facet whose value path starts with a collection navigation (<c>it.Lines.Id</c>): the facet builder cannot group by it.</summary>
    internal static void RefuseCollectionPaths(Type entityType)
    {
        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var facet in property.GetCustomAttributes<ProvideFacetAttribute>(inherit: true).Where(f => !string.IsNullOrWhiteSpace(f.ValuePath)))
            {
                var path = facet.ValuePath!.Replace('/', '.');
                var first = (path.StartsWith("it.", StringComparison.Ordinal) ? path[3..] : path).Split('.')[0];
                if (entityType.GetProperty(first, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.PropertyType is { } type
                    && type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                {
                    throw new InvalidOperationException(
                        $"The facet {entityType.Name}.{property.Name} reads {facet.ValuePath} through the collection {first}; a facet value must be a single value of the row or of a reference.");
                }
            }
        }
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();

    private static ODataNavigationScope ScopeOf<T>(HttpContext http) =>
        http.Items[ScopedEnableQueryAttribute.ScopeItem] as ODataNavigationScope ?? ODataNavigationScope.For(http.RequestServices, typeof(T));

    /// <summary>The rows matching an OData $filter, read the way the entity set's own list reads it, navigations included.</summary>
    public static async Task<IQueryable<T>> FilterAsync<T>(IQueryable<T> rows, string? filter, HttpContext request, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return rows;
        }

        var services = request.RequestServices;
        var http = new DefaultHttpContext { RequestServices = services };
        http.ODataFeature().RoutePrefix = CoworkeeODataModule.RoutePrefix;
        http.Request.QueryString = QueryString.Create("$filter", filter);
        return await ScopeOf<T>(request).ApplyAsync(rows.ApplyODataFilter(http.Request.ODataQueryOptions<T>(services.GetRequiredService<IEdmModel>())), cancellationToken);
    }
}
