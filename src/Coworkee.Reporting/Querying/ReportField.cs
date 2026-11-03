using System.Reflection;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Querying;

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
