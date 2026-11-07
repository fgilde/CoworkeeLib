using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite;

/// <summary>The copy of one set: a table "rows" with a column per property of <typeparamref name="T"/>.</summary>
internal sealed class CopyDb<T>(DbContextOptions<CopyDb<T>> options) : DbContext(options)
    where T : class
{
    public const string Table = "rows";

    protected override void OnModelCreating(ModelBuilder modelBuilder) => CopyModel.Configure<T>(modelBuilder);
}

/// <summary>
/// How a row type becomes a table: plain values are columns (indexed after a download, like the columns of the in-memory copy), DateTimeOffset a sortable
/// 64-bit value, decimal a double (SQLite compares neither natively), collections and nested objects JSON. Filters on JSON columns go to the server.
/// </summary>
internal static class CopyModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly MethodInfo JsonConverterMethod = typeof(CopyModel).GetMethod(nameof(JsonConverter), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static IReadOnlyList<PropertyInfo> Properties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.SetMethod is not null && p.GetIndexParameters().Length == 0)];

    public static bool IsColumn(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        return plain.IsPrimitive || plain.IsEnum || plain == typeof(string) || plain == typeof(Guid) || plain == typeof(decimal) || plain == typeof(DateTime)
            || plain == typeof(DateTimeOffset) || plain == typeof(DateOnly) || plain == typeof(TimeOnly) || plain == typeof(TimeSpan);
    }

    public static void Configure<T>(ModelBuilder model)
        where T : class
    {
        var entity = model.Entity<T>();
        entity.ToTable(CopyDb<T>.Table);
        entity.HasKey("Id");
        foreach (var property in Properties(typeof(T)))
        {
            var plain = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var column = entity.Property(property.PropertyType, property.Name);
            if (!IsColumn(property.PropertyType))
            {
                column.HasConversion((ValueConverter)JsonConverterMethod.MakeGenericMethod(property.PropertyType).Invoke(null, null)!);
                continue;
            }

            if (plain == typeof(DateTimeOffset))
            {
                // EF never hands a null to a converter: the one of DateTimeOffset serves DateTimeOffset? too
                column.HasConversion(new DateTimeOffsetToBinaryConverter());
            }
            else if (plain == typeof(decimal))
            {
                column.HasConversion<double>();
            }
            else if (plain.IsEnum)
            {
                // by name: the server writes snapshots without knowing the client's enum values
                column.HasConversion<string>();
            }

        }
    }

    private static ValueConverter JsonConverter<TValue>() =>
        new ValueConverter<TValue, string>(v => JsonSerializer.Serialize(v, Json), s => JsonSerializer.Deserialize<TValue>(s, Json)!);

    /// <summary>How the server writes a column of a snapshot, as this model stores it.</summary>
    public static string Kind(Type type)
    {
        if (!IsColumn(type))
        {
            return Contracts.Data.ClientEntitySnapshotKinds.Json;
        }

        var plain = Nullable.GetUnderlyingType(type) ?? type;
        return plain == typeof(DateTimeOffset) ? Contracts.Data.ClientEntitySnapshotKinds.DateTimeOffset
            : plain == typeof(DateTime) ? Contracts.Data.ClientEntitySnapshotKinds.DateTime
            : plain == typeof(Guid) ? Contracts.Data.ClientEntitySnapshotKinds.Guid
            : plain == typeof(bool) ? Contracts.Data.ClientEntitySnapshotKinds.Boolean
            : plain == typeof(decimal) || plain == typeof(double) || plain == typeof(float) ? Contracts.Data.ClientEntitySnapshotKinds.Real
            : plain.IsPrimitive ? Contracts.Data.ClientEntitySnapshotKinds.Integer
            : Contracts.Data.ClientEntitySnapshotKinds.Text;
    }

    /// <summary>Reads a property of a row, boxed.</summary>
    public static Func<object, object?> Getter(PropertyInfo property)
    {
        var row = Expression.Parameter(typeof(object), "row");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Property(Expression.Convert(row, property.DeclaringType!), property), typeof(object)), row).Compile();
    }
}
