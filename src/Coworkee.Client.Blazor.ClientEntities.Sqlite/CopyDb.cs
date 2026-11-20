using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite;

/// <summary>The copy of one set: a table "rows" with a column per property of <typeparamref name="T"/>.</summary>
internal sealed class CopyDb<T>(DbContextOptions<CopyDb<T>> options) : DbContext(options)
    where T : class
{
    public const string Table = "rows";

    protected override void OnModelCreating(ModelBuilder modelBuilder) => CopyModel.Configure<T>(modelBuilder);

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(EmptyJsonColumns<T>.Instance);
}

/// <summary>
/// A NULL JSON column (a collection outside the expand of a server snapshot, which selects no navigation) reads as an empty collection where the
/// property is not nullable, so the row reads as the server's JSON would; nullable ones and nested objects stay null.
/// </summary>
internal sealed class EmptyJsonColumns<T> : IMaterializationInterceptor
    where T : class
{
    public static readonly EmptyJsonColumns<T> Instance = new();

    private static readonly Action<object>? Fill = Build();

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        Fill?.Invoke(entity);
        return entity;
    }

    // row => { if (row.A == null) row.A = FromJson<A>("[]"); ... } for every such property, compiled once
    private static Action<object>? Build()
    {
        var nullability = new NullabilityInfoContext();
        var row = Expression.Parameter(typeof(object), "row");
        var typed = Expression.Convert(row, typeof(T));
        var fills = CopyModel.Properties(typeof(T))
            .Where(p => !CopyModel.IsColumn(p.PropertyType) && !p.PropertyType.IsValueType && nullability.Create(p).WriteState != NullabilityState.Nullable)
            .Select(p => (Property: p, Json: CopyModel.EmptyJson(p.PropertyType)))
            .Where(x => x.Json is not null)
            .Select(x => (Expression)Expression.IfThen(
                Expression.Equal(Expression.Property(typed, x.Property), Expression.Constant(null, x.Property.PropertyType)),
                Expression.Assign(Expression.Property(typed, x.Property), Expression.Call(CopyModel.FromJsonMethod.MakeGenericMethod(x.Property.PropertyType), Expression.Constant(x.Json)))))
            .ToList();
        return fills.Count == 0 ? null : Expression.Lambda<Action<object>>(Expression.Block(fills), row).Compile();
    }
}

/// <summary>
/// How a row type becomes a table: plain values are columns (indexed after a download, like the columns of the in-memory copy), DateTimeOffset a sortable
/// 64-bit value, decimal a double (SQLite compares neither natively), TimeOnly and TimeSpan their ticks, collections and nested objects JSON. Filters on
/// JSON columns go to the server. The server writes snapshots in the same formats (<see cref="Kind"/>).
/// </summary>
internal static class CopyModel
{
    private static readonly JsonSerializerOptions Json = Contracts.CoworkeeJson.Web;
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
                // NULL where the copy has no value (EmptyJsonColumns): EF reads it without the converter
                column.IsRequired(false).HasConversion((ValueConverter)JsonConverterMethod.MakeGenericMethod(property.PropertyType).Invoke(null, null)!);
                continue;
            }

            if (plain == typeof(DateTimeOffset))
            {
                // EF never hands a null to a converter: the one of DateTimeOffset serves DateTimeOffset? too
                column.HasConversion(new ValueConverter<DateTimeOffset, long>(
                    v => Contracts.Data.ClientEntitySnapshotKinds.ToInstant(v), v => Contracts.Data.ClientEntitySnapshotKinds.FromInstant(v)));
            }
            else if (plain == typeof(decimal))
            {
                column.HasConversion<double>();
            }
            else if (plain == typeof(TimeOnly) || plain == typeof(TimeSpan))
            {
                column.HasConversion<long>();
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

    public static readonly MethodInfo FromJsonMethod = typeof(CopyModel).GetMethod(nameof(FromJson), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static TValue FromJson<TValue>(string json) => JsonSerializer.Deserialize<TValue>(json, Json)!;

    /// <summary>"{}" for a dictionary, "[]" for any other collection, null for an object.</summary>
    public static string? EmptyJson(Type type) =>
        type == typeof(string) || !typeof(System.Collections.IEnumerable).IsAssignableFrom(type) ? null
        : type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() is var d && (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>))) ? "{}"
        : "[]";

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
            : plain == typeof(DateOnly) ? Contracts.Data.ClientEntitySnapshotKinds.Date
            : plain == typeof(TimeOnly) || plain == typeof(TimeSpan) ? Contracts.Data.ClientEntitySnapshotKinds.Ticks
            : plain == typeof(char) ? Contracts.Data.ClientEntitySnapshotKinds.Text
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
