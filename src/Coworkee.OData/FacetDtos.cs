using System.Reflection;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Nextended.Core.Facets;

namespace Coworkee.OData;

/// <summary>
/// Turns the facets of Nextended's builder into <see cref="FacetGroupDto"/>s: enum values by member name with their display labels,
/// flags split into single flags, yes/no for booleans, and the behavior of [FacetBehavior] or ConfigureFacet.
/// </summary>
internal static class FacetDtos
{
    public static FacetGroupDto From(FacetGroup group, Type entityType, ODataEntityRegistry registry)
    {
        var property = entityType.GetProperty(group.Field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var behavior = property is null ? null : registry.FacetBehavior(entityType, property);
        var valueType = ValueType(group, property);
        var kind = Kind(group, valueType);
        IEnumerable<FacetOptionDto> options = kind switch
        {
            FacetValueKind.Enum => group.Options.Select(o => EnumOption(group, valueType, o)),
            FacetValueKind.Flags => FlagOptions(group, valueType),
            FacetValueKind.Boolean => group.Options.Select(o => Option(o) with
            {
                Value = JsonSerializer.SerializeToElement(FacetLiterals.Flag(o.Value) ? "true" : "false"),
                Label = FacetLiterals.Flag(o.Value) ? "Yes" : "No",
            }),
            _ => group.Options.Select(Option),
        };
        return new FacetGroupDto(group.Key, group.Label, group.Field, (FacetKind)(int)group.Type, (FacetOperator)(int)group.GroupOperator, [.. options], group.MultiSelect, group.Order)
        {
            ValuePath = group.ValuePath,
            ValueKind = kind,
            View = behavior?.View ?? FacetView.Auto,
            Combine = behavior?.ConfiguredCombine ?? (!group.MultiSelect ? FacetCombine.Single : group.GroupOperator == FacetGroupOperator.And ? FacetCombine.All : FacetCombine.Any),
            AllowExclude = behavior?.AllowExclude ?? false,
            Switchable = behavior?.Switchable ?? false,
            TreeSeparator = behavior?.TreeSeparator,
        };
    }

    /// <summary>The type the options filter on, without <c>Nullable</c>: the value path's type for references, else the property's.</summary>
    public static Type ValueType(FacetGroup group, PropertyInfo? property)
    {
        var type = (group.ValueClrType is { } name ? Type.GetType(name) : null) ?? property?.PropertyType ?? typeof(string);
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    public static FacetValueKind Kind(FacetGroup group, Type type) => type switch
    {
        _ when group.LabelPath is not null => FacetValueKind.Reference,
        _ when type.IsEnum => type.IsDefined(typeof(FlagsAttribute), false) ? FacetValueKind.Flags : FacetValueKind.Enum,
        _ when type == typeof(bool) => FacetValueKind.Boolean,
        _ when type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) => FacetValueKind.Date,
        _ when type.IsPrimitive && type != typeof(char) || type == typeof(decimal) => FacetValueKind.Number,
        _ => FacetValueKind.Text,
    };

    private static FacetOptionDto Option(FacetOption option) =>
        new(JsonSerializer.SerializeToElement(option.Value), option.Label, option.Enabled, (int)(option.Count ?? 0), option.OData) { Meta = option.Meta };

    // the builder groups by the stored value, which is the number for enums kept as integers
    private static FacetOptionDto EnumOption(FacetGroup group, Type type, FacetOption option)
    {
        if (!Enum.TryParse(type, option.Value, ignoreCase: true, out var value))
        {
            return Option(option);
        }

        var name = value!.ToString()!;
        return Option(option) with
        {
            Value = JsonSerializer.SerializeToElement(name),
            Label = EnumLabels.Of(type, name),
            OData = $"{group.ValuePath} eq '{name}'",
            Meta = EnumLabels.ColorOf(type, name) is { } color ? new Dictionary<string, string> { ["color"] = color } : option.Meta,
        };
    }

    // stored combinations ("Express, Gift") count towards each of their flags
    private static IEnumerable<FacetOptionDto> FlagOptions(FacetGroup group, Type type)
    {
        var combinations = group.Options
            .Select(o => (Ok: Enum.TryParse(type, o.Value, ignoreCase: true, out var value), Bits: Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture), Count: o.Count ?? 0))
            .Where(c => c.Ok)
            .ToList();
        foreach (var flag in Enum.GetValues(type).Cast<Enum>())
        {
            var bits = Convert.ToInt64(flag, System.Globalization.CultureInfo.InvariantCulture);
            if (bits == 0 || (bits & (bits - 1)) != 0)
            {
                continue;
            }

            var name = flag.ToString();
            var count = combinations.Where(c => (c.Bits & bits) == bits).Sum(c => c.Count);
            yield return new FacetOptionDto(JsonSerializer.SerializeToElement(name), EnumLabels.Of(type, name), count > 0, (int)count, $"{group.ValuePath} has '{name}'")
            {
                Meta = EnumLabels.ColorOf(type, name) is { } color ? new Dictionary<string, string> { ["color"] = color } : null,
            };
        }
    }
}
