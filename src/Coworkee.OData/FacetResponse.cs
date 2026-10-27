using System.Reflection;
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
        var denied = await ODataEntityAccess.DeniedPropertiesAsync(services, typeof(T), cancellationToken);
        foreach (var facet in facets.Where(f => !denied.Contains(f.Field)))
        {
            var own = context is not null && context.Groups.ContainsKey(facet.Key) && context.Combines?.GetValueOrDefault(facet.Key) != FacetCombine.All;
            var rows = own ? Filter(visible, context!.Without(facet.Key), http) : filtered;
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

        foreach (var property in services.GetRequiredService<Computed.ODataComputedProperties>().Of(typeof(T)).Where(p => p.Facet is not null))
        {
            if (property.Permission is null || await services.GetRequiredService<Coworkee.Application.Authorization.IPermissionChecker>().IsGrantedAsync(property.Permission, cancellationToken))
            {
                var own = context is not null && context.Groups.ContainsKey(property.Name);
                result.Add(await ComputedAsync(property, own ? Filter(visible, context!.Without(property.Name), http) : filtered, http, cancellationToken));
            }
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
            var count = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(Filter(rows, option.OData, http), cancellationToken);
            options.Add(new FacetOptionDto(System.Text.Json.JsonSerializer.SerializeToElement(option.Value), option.Label, count > 0, count, option.OData)
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

    /// <summary>The rows matching an OData $filter, read the way the entity set's own list reads it, navigations included.</summary>
    public static IQueryable<T> Filter<T>(IQueryable<T> rows, string? filter, HttpContext request)
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
        var filtered = rows.ApplyODataFilter(http.Request.ODataQueryOptions<T>(services.GetRequiredService<IEdmModel>()));
        return request.Items[ScopedEnableQueryAttribute.ScopeItem] is ODataNavigationScope scope ? scope.Apply(filtered) : filtered;
    }
}
