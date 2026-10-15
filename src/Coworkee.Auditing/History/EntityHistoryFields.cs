using System.Reflection;
using System.Text.Json;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Coworkee.Auditing.History;

/// <summary>The fields of a record as the audit writes them (JSON per property) and which of them a restore may set.</summary>
internal sealed class EntityHistoryFields(IEntityType type, IReadOnlyCollection<string> hidden)
{
    public const string Masked = "\"***\"";

    private static readonly HashSet<string> Bookkeeping = new(StringComparer.Ordinal)
    {
        nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy), nameof(IAuditable.ModifiedAt), nameof(IAuditable.ModifiedBy),
        nameof(ISoftDelete.IsDeleted), nameof(ISoftDelete.DeletedAt), nameof(ISoftDelete.DeletedBy), nameof(IHasConcurrencyToken.Version),
        nameof(IMultiTenant.TenantId), nameof(IVersioned.Revision),
    };

    public bool IsHidden(string property) => hidden.Contains(property, StringComparer.Ordinal);

    public IProperty? Restorable(string property) =>
        type.FindProperty(property) is { PropertyInfo.CanWrite: true } p && !p.IsShadowProperty() && !p.IsKey() && !p.IsConcurrencyToken
        && !Bookkeeping.Contains(p.Name) && !IsHidden(p.Name) && !IsSensitive(p)
            ? p
            : null;

    public IEnumerable<IProperty> Properties => type.GetProperties().Where(p => Restorable(p.Name) is not null);

    public Dictionary<string, string?> Read(object entity) =>
        Properties.ToDictionary(p => p.Name, p => Json(p.PropertyInfo!.GetValue(entity)), StringComparer.Ordinal);

    public void Write(object entity, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var property in Properties)
        {
            if (values.TryGetValue(property.Name, out var json))
            {
                property.PropertyInfo!.SetValue(entity, Value(property, json));
            }
        }
    }

    /// <summary>Whether the recorded value can be written back: masked values and null for a required value cannot.</summary>
    public bool CanTake(IProperty property, string? json) =>
        json != Masked && (json is not null || property.IsNullable || Nullable.GetUnderlyingType(property.ClrType) is not null || !property.ClrType.IsValueType);

    public bool Same(IProperty property, string? left, string? right) =>
        left == right || Equals(Value(property, left), Value(property, right));

    public static object? Value(IProperty property, string? json) =>
        json is null ? null : JsonSerializer.Deserialize(json, property.ClrType);

    public static string? Json(object? value) => value is null ? null : JsonSerializer.Serialize(value);

    private static bool IsSensitive(IProperty property) =>
        // the annotation of AuditPropertyBuilderExtensions.IsSensitive, whose name is internal to the infrastructure
        property.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is not null || property.FindAnnotation("Coworkee:Sensitive")?.Value is true;
}
