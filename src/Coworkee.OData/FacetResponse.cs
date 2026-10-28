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

        var scoped = await ScopedAsync(http, filtered, cancellationToken);
        var prepared = Paths(typeof(T));
        foreach (var facet in facets.Where(f => !denied.Contains(f.Field)))
        {
            if (!Checked(facet, prepared) || await RefusedAsync(visible, facet, http, cancellationToken))
            {
                continue;
            }

            var own = context is not null && context.Groups.ContainsKey(facet.Key) && context.Combines?.GetValueOrDefault(facet.Key) != FacetCombine.All;
            var rows = own ? ScopedQueryProvider.Wrap(await FilterAsync(visible, context!.Without(facet.Key), http, cancellationToken), ScopeOf<T>(http)) : scoped;
            var group = facet;
            if (own && facet.Type is not (FacetType.Range or FacetType.DateRange))
            {
                // ponytail: one build per chosen group computes every group; build only the asked group when facets get many
                var rebuilt = await services.GetRequiredService<IFacetBuilder>().BuildAsync(rows, cancellationToken);
                group = rebuilt.FirstOrDefault(g => g.Key == facet.Key) ?? facet;
            }

            var dto = FacetDtos.From(group, typeof(T), registry);
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
    /// The rows for the facet builder: the navigations it reads (value and label paths, the choices of other groups) show only the rows their
    /// target's set lists for the user. A row whose target is hidden has no value there, as <c>$expand</c> shows null: it is left out of the
    /// group, and a label read through a hidden target shows the key.
    /// </summary>
    internal static async Task<IQueryable<T>> ScopedAsync<T>(HttpContext http, IQueryable<T> rows, CancellationToken cancellationToken)
        where T : class
    {
        var scope = ScopeOf<T>(http);
        foreach (var path in Paths(typeof(T)).Order(StringComparer.Ordinal))
        {
            await scope.PrepareAsync(rows.Select($"it.{path}").Expression, cancellationToken);
        }

        return ScopedQueryProvider.Wrap(rows, scope);
    }

    /// <summary>The value and label paths of the facets of a type as the facet builder reads them (its own attribute scan), in Dynamic LINQ form without "it.".</summary>
    internal static HashSet<string> Paths(Type entityType) => entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .SelectMany(p => p.GetCustomAttributes<ProvideFacetAttribute>(inherit: true)
            .SelectMany(f => new[] { string.IsNullOrWhiteSpace(f.ValuePath) ? p.Name : f.ValuePath, string.IsNullOrWhiteSpace(f.LabelPath) ? null : f.LabelPath }))
        .OfType<string>()
        .Select(Normalize)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether the scope checked the paths of a group before the builder ran (<see cref="Paths"/>). The builder's query shows no rows of a target nobody checked,
    /// so a group it got elsewhere would count wrongly: it is left out instead of failing the request.
    /// </summary>
    internal static bool Checked(FacetGroup facet, IReadOnlySet<string> paths) =>
        paths.Contains(Normalize(facet.ValuePath ?? facet.Field)) && (facet.LabelPath is not { } label || paths.Contains(Normalize(label)));

    private static string Normalize(string path)
    {
        var dotted = path.Replace('/', '.');
        return dotted.StartsWith("it.", StringComparison.Ordinal) ? dotted[3..] : dotted;
    }

    /// <summary>
    /// Whether the user may not have a facet: its value or label path reads a member denied to him (also one of the row itself other than the facet's
    /// property, or one behind a navigation), or a path the checks cannot parse (a label that is an expression); or its value goes through a navigation
    /// into a set he may not read or whose row filters do more than <c>Where</c> (they cannot apply inside this query). Such a group is left out. Targets
    /// with row filters stay: the builder counted their visible rows only (<see cref="ScopedAsync{T}"/>).
    /// </summary>
    private static async Task<bool> RefusedAsync<T>(IQueryable<T> rows, FacetGroup facet, HttpContext http, CancellationToken cancellationToken)
        where T : class
    {
        var denied = http.Items[ODataEntityAccess.DeniedItem] as Dictionary<string, IReadOnlySet<string>> ?? await ODataEntityAccess.DeniedByTypeAsync(http.RequestServices, cancellationToken);
        foreach (var read in new[] { facet.ValuePath ?? facet.Field, facet.LabelPath }.OfType<string>())
        {
            try
            {
                if (ODataQueryGuard.Denies(http.RequestServices, typeof(T), $"{Normalize(read).Replace('.', '/')} ne null", denied))
                {
                    return true;
                }
            }
            catch (Microsoft.OData.ODataException)
            {
                return true;
            }
        }

        var path = Normalize(facet.ValuePath ?? facet.Field);
        if (!path.Contains('.'))
        {
            return false;
        }

        var value = rows.Select($"it.{path}");
        var scope = ScopeOf<T>(http);
        await scope.PrepareAsync(value.Expression, cancellationToken);
        return scope.Refuses(value.Expression);
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
