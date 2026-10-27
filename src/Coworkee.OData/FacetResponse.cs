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

        // ponytail: a value read through a navigation would group over the target's hidden rows, so such groups are left out; scope them like labels if needed
        foreach (var facet in facets.Where(f => !denied.Contains(f.Field) && !ThroughNavigation(typeof(T), f.ValuePath)))
        {
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

    private static bool ThroughNavigation(Type entityType, string? path) =>
        (path?.StartsWith("it/", StringComparison.Ordinal) == true ? path[3..] : path)?.Split('/') is [var first, _, ..]
        && entityType.GetProperty(first, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { } property
        && !Transfer.Import.ImportField.IsSimple(property.PropertyType);

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
