using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Reporting;
using Coworkee.OData;
using Microsoft.OData.Edm;
using Nextended.Core.Facets;

namespace Coworkee.Reporting.Querying;

/// <summary>A registered entity set or app source with the fields reports may use; a field behind a navigation needs the permission of its set too.</summary>
/// <param name="Computed">The app's source, null for an entity set.</param>
internal sealed record ReportSource(string EntitySet, Type RowType, string? Permission, string Label, IReadOnlyList<ReportField> Fields, Sources.ReportSourceRegistration? Computed = null)
{
    public ReportField? Find(string? path) => Fields.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
}

/// <param name="Permission">The permission of the set a navigation leads to.</param>
/// <param name="PropertyPermission">The permission a protected property needs (HideUnless).</param>
internal sealed record ReportField(string Path, string Label, ReportFieldKind Kind, IReadOnlyList<PropertyInfo> Chain, string? EnumType, string? Permission, string? PropertyPermission = null)
{
    public bool Allowed(IReadOnlySet<string> granted) =>
        (Permission is null || granted.Contains(Permission)) && (PropertyPermission is null || granted.Contains(PropertyPermission));

    public Type ClrType => Chain[^1].PropertyType;

    public bool IsNumeric => Kind is ReportFieldKind.Integer or ReportFieldKind.Number;

    public ReportFieldDto ToDto() =>
        new(Path, Label, Kind, Kind == ReportFieldKind.Enum ? Enum.GetNames(Nullable.GetUnderlyingType(ClrType) ?? ClrType) : null, EnumType,
            (Nullable.GetUnderlyingType(ClrType) ?? ClrType) == typeof(DateOnly),
            Kind == ReportFieldKind.Enum ? [.. Enum.GetNames(Nullable.GetUnderlyingType(ClrType) ?? ClrType).Select(n => Contracts.Data.EnumLabels.Of(Nullable.GetUnderlyingType(ClrType) ?? ClrType, n))] : null);
}

/// <summary>Reads fields from the EDM, so properties hidden from OData stay hidden from reports.</summary>
internal sealed partial class ReportSchema(ODataEntityRegistry registry, IEdmModel model, IEnumerable<Sources.ReportSourceRegistration> computed)
{
    private readonly ConcurrentDictionary<string, ReportSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<Sources.ReportSourceRegistration> _computed = Unique(registry, computed);

    public IEnumerable<ReportSource> All => registry.Entities.Select(Source).Concat(_computed.Select(Source));

    public ReportSource? Find(string entitySet) =>
        registry.Entities.FirstOrDefault(e => string.Equals(e.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase)) is { } entity ? Source(entity)
        : _computed.FirstOrDefault(c => string.Equals(c.Name, entitySet, StringComparison.OrdinalIgnoreCase)) is { } source ? Source(source)
        : null;

    private ReportSource Source(ODataEntityRegistration entity) => _sources.GetOrAdd(entity.EntitySet, _ => Build(entity));

    private ReportSource Source(Sources.ReportSourceRegistration source) =>
        _sources.GetOrAdd(source.Name, _ => new ReportSource(source.Name, source.RowType, source.Permission, source.Label,
            [.. Scalars(source.EdmType, source.RowType, [], null, null, new Dictionary<string, string>())], source));

    private static List<Sources.ReportSourceRegistration> Unique(ODataEntityRegistry registry, IEnumerable<Sources.ReportSourceRegistration> computed)
    {
        var list = computed.ToList();
        var clash = list.Select(c => c.Name).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key
            ?? list.FirstOrDefault(c => registry.Entities.Any(e => string.Equals(e.EntitySet, c.Name, StringComparison.OrdinalIgnoreCase)))?.Name;
        return clash is null ? list : throw new InvalidOperationException($"The report source '{clash}' is registered twice or clashes with an OData set.");
    }

    private ReportSource Build(ODataEntityRegistration entity)
    {
        var fields = new List<ReportField>();
        if (model.FindDeclaredType(entity.EntityType.FullName) is IEdmEntityType type)
        {
            fields.AddRange(Scalars(type, entity.EntityType, [], null, null, entity.ProtectedProperties));
            foreach (var navigation in type.NavigationProperties().Where(n => n.TargetMultiplicity() != EdmMultiplicity.Many))
            {
                var property = entity.EntityType.GetProperty(navigation.Name);
                if (property is not null && registry.Find(property.PropertyType) is { } target && model.FindDeclaredType(property.PropertyType.FullName) is IEdmEntityType targetType)
                {
                    fields.AddRange(Scalars(targetType, property.PropertyType, [property], Label(property), target.Permission, target.ProtectedProperties));
                }
            }
        }

        return new ReportSource(entity.EntitySet, entity.EntityType, entity.Permission, Humanize(entity.EntitySet), fields);
    }

    private static IEnumerable<ReportField> Scalars(
        IEdmStructuredType type, Type clrType, PropertyInfo[] prefix, string? prefixLabel, string? permission, IReadOnlyDictionary<string, string> protectedProperties)
    {
        foreach (var edm in type.StructuralProperties().Where(p => p.Type.IsPrimitive() || p.Type.IsEnum()))
        {
            if (edm.Name == "TenantId" || clrType.GetProperty(edm.Name) is not { } property || Kind(property.PropertyType) is not { } kind)
            {
                continue;
            }

            var label = Label(property);
            yield return new ReportField(
                string.Join('/', prefix.Select(p => p.Name).Append(property.Name)),
                prefixLabel is null ? label : $"{prefixLabel} {char.ToLowerInvariant(label[0])}{label[1..]}",
                kind,
                [.. prefix, property],
                edm.Type.IsEnum() ? edm.Type.FullName() : null,
                permission,
                protectedProperties.GetValueOrDefault(property.Name));
        }
    }

    private static ReportFieldKind? Kind(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type switch
        {
            _ when type.IsEnum => ReportFieldKind.Enum,
            _ when type == typeof(string) => ReportFieldKind.Text,
            _ when type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) => ReportFieldKind.Integer,
            _ when type == typeof(decimal) || type == typeof(double) || type == typeof(float) => ReportFieldKind.Number,
            _ when type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(DateOnly) => ReportFieldKind.Date,
            _ when type == typeof(bool) => ReportFieldKind.Boolean,
            _ when type == typeof(Guid) => ReportFieldKind.Guid,
            _ => null,
        };
    }

    private static string Label(PropertyInfo property) =>
        property.GetCustomAttribute<ProvideFacetAttribute>()?.Label
        ?? property.GetCustomAttribute<DisplayAttribute>()?.GetName()
        ?? property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
        ?? Humanize(property.Name);

    internal static string Humanize(string name)
    {
        var words = Words().Split(name).Where(w => w.Length > 0).ToList();
        return string.Join(' ', words.Select((w, i) => i == 0 ? w : w.ToLowerInvariant()));
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|_")]
    private static partial Regex Words();
}
