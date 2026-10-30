using System.Globalization;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Reflection;
using Coworkee.OData.Computed;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nextended.Core.Facets;

namespace Coworkee.OData;

/// <summary>
/// The options of facet groups. Nextended's builder declares the groups; their options come from its list query (one <c>GROUP BY</c> per group, all in
/// one <c>UNION ALL</c>), here for the groups that need the database. A bounded set (<see cref="CoworkeeODataOptions.FacetRowLimit"/>) reads the members
/// of the row its other groups need once (<see cref="ReadAsync{T}"/>) and counts them in memory: list groups, ranges and the options of computed properties.
/// </summary>
internal static class FacetCounts
{
    /// <summary>The options of list groups as the builder counts them: one statement on <paramref name="rows"/> for all of them.</summary>
    public static async Task FromDatabaseAsync<T>(IQueryable<T> rows, IReadOnlyList<FacetGroup> groups, CancellationToken cancellationToken)
    {
        IQueryable<Counted>? all = null;
        foreach (var group in groups)
        {
            var path = group.ValuePath ?? group.Field;
            var (value, label) = (Dynamic(path), Dynamic(group.LabelPath ?? path));
            IQueryable query = rows.Where($"{value} != null").GroupBy($"new ({value} as value, {label} as label)").OrderBy("Count() desc");
            if (Top(typeof(T), group) is { } top)
            {
                query = query.Take(top);
            }

            var part = query.Select<Counted>($"new (\"{group.Key}\" as Group, As(Key.value, \"string\") as Value, As(Key.label, \"string\") as Label, Count() as Count)");
            all = all is null ? part : all.Concat(part);
        }

        if (all is not null)
        {
            Fill(groups, await all.ToListAsync(cancellationToken));
        }
    }

    public static bool IsList(FacetGroup group) => group.Type is FacetType.CheckboxList or FacetType.TokenList or FacetType.Radio;

    public static bool IsRange(FacetGroup group) => group.Type is FacetType.Range or FacetType.DateRange;

    /// <summary>
    /// The rows of a bounded set with the members the groups and computed options read: null when there are more than the limit, nothing to count
    /// locally, or the row cannot be built. Reads <paramref name="filtered"/>, which the navigation scope rewrote already: the members are the row's own,
    /// and the options' filters come scoped from <see cref="FacetResponse.FilterAsync{T}"/>.
    /// </summary>
    public static async Task<Read<T>?> ReadAsync<T>(HttpContext http, IQueryable<T> filtered, IReadOnlyList<FacetGroup> groups, IReadOnlyList<ODataComputedProperty> computed,
        CancellationToken cancellationToken)
        where T : class
    {
        var limit = http.RequestServices.GetRequiredService<IOptions<CoworkeeODataOptions>>().Value.FacetRowLimit;
        if (limit <= 0 || typeof(T).IsAbstract || typeof(T).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is not { } create)
        {
            return null;
        }

        var lists = groups.Where(g => IsList(g) && Listed(typeof(T), g) is not null).ToList();
        var ranges = groups.Where(g => IsRange(g) && Ranged(typeof(T), g) is not null).ToList();
        var options = new List<(ODataComputedProperty Property, LambdaExpression[] Filters)>();
        foreach (var property in computed)
        {
            var filters = new List<LambdaExpression>();
            foreach (var option in property.Facet!.Options)
            {
                if (Predicate(filtered, await FacetResponse.FilterAsync(filtered, option.OData, http, cancellationToken, clrNames: true)) is { } filter)
                {
                    filters.Add(filter);
                }
            }

            if (filters.Count == property.Facet.Options.Count)
            {
                options.Add((property, [.. filters]));
            }
        }

        if (lists.Count + ranges.Count + options.Count == 0)
        {
            return null;
        }

        var it = Expression.Parameter(typeof(T), "it");
        var members = lists.SelectMany(g => Listed(typeof(T), g)!).Concat(ranges.Select(g => Ranged(typeof(T), g)!)).Distinct();
        var row = Expression.MemberInit(Expression.New(create), members.Select(m => Expression.Bind(m, Expression.Property(it, m))));
        var matches = Expression.NewArrayInit(typeof(bool), options.SelectMany(o => o.Filters).Select(f => ReplacingExpressionVisitor.Replace(f.Parameters[0], it, f.Body)));
        var shape = Expression.Lambda<Func<T, Row<T>>>(Expression.New(typeof(Row<T>).GetConstructors()[0], row, matches), it);
        var loaded = await filtered.Select(shape).Take(limit + 1).ToListAsync(cancellationToken);
        if (loaded.Count > limit)
        {
            return null;
        }

        var items = loaded.Select(r => r.Item).ToList();
        var counted = new List<Counted>();
        foreach (var group in lists)
        {
            var read = Listed(typeof(T), group)!;
            var (value, label) = (Getter<T>(read[0]), Getter<T>(read[^1]));
            // ponytail: groups ordinally; a case-insensitive collation (SQL Server's default) or trailing blanks merge values in the database, not here
            var grouped = items.Select(i => (Value: value(i), Label: label(i))).Where(v => v.Value is not null).GroupBy(v => v).OrderByDescending(g => g.Count());
            counted.AddRange((Top(typeof(T), group) is { } top ? grouped.Take(top) : grouped)
                .Select(g => new Counted { Group = group.Key, Value = Text(g.Key.Value)!, Label = Text(g.Key.Label), Count = g.Count() }));
        }

        Fill(lists, counted);
        var first = 0;
        var computedCounts = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var (property, filters) in options)
        {
            var at = first;
            computedCounts[property.Name] = [.. filters.Select((_, i) => loaded.Count(r => r.Matches[at + i]))];
            first += filters.Length;
        }

        return new Read<T>(items.AsQueryable(), lists.ToHashSet(), ranges.ToHashSet(), computedCounts);
    }

    /// <summary>What <see cref="ReadAsync{T}"/> counted: the list groups (options filled), the range groups the rows serve, the counts of computed options.</summary>
    public sealed record Read<T>(IQueryable<T> Rows, IReadOnlySet<FacetGroup> Lists, IReadOnlySet<FacetGroup> Ranges, IReadOnlyDictionary<string, int[]> Computed);

    // the projection: the row with the read members, and whether it matches each computed option
    private sealed class Row<T>(T item, bool[] matches)
    {
        public T Item { get; } = item;

        public bool[] Matches { get; } = matches;
    }

    // the condition of the single Where a filter added to the rows; null when it is anything else
    private static LambdaExpression? Predicate<T>(IQueryable<T> rows, IQueryable<T> filtered) =>
        filtered.Expression is MethodCallExpression { Method.Name: nameof(Queryable.Where) } call && call.Method.DeclaringType == typeof(Queryable)
        && call.Arguments[0] == rows.Expression && call.Arguments[1] is UnaryExpression { Operand: LambdaExpression { Parameters.Count: 1 } predicate }
            ? predicate
            : null;

    // the value (and label) member of a list group, when both are the row's own and read the same in memory as the database writes them as text
    private static PropertyInfo[]? Listed(Type entity, FacetGroup group)
    {
        if (Own(entity, group.ValuePath ?? group.Field) is not { } value || Type.GetType(group.ValueClrType ?? "") is not { } clr)
        {
            return null;
        }

        var type = Nullable.GetUnderlyingType(value.PropertyType) ?? value.PropertyType;
        var same = type == typeof(string) || ((type == typeof(bool) || type.IsEnum || type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            && (Nullable.GetUnderlyingType(clr) ?? clr) == type);
        if (!same)
        {
            return null;
        }

        return group.LabelPath is null ? [value] : Own(entity, group.LabelPath) is { } label && label.PropertyType == typeof(string) ? [value, label] : null;
    }

    // the member of a range group the rows serve: numbers and dates compare alike in memory (not double/float: the database rounds them to decimal)
    private static PropertyInfo? Ranged(Type entity, FacetGroup group) =>
        Own(entity, group.ValuePath ?? group.Field) is { } member && (Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType) is var type
        && (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly))
            ? member
            : null;

    // a settable property of the row itself: no navigation
    private static PropertyInfo? Own(Type entity, string? path)
    {
        var name = Dynamic(path ?? "");
        name = name.StartsWith("it.", StringComparison.Ordinal) ? name[3..] : name;
        if (name.Length == 0 || name.Contains('.'))
        {
            return null;
        }

        try
        {
            return entity.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { SetMethod: not null } property ? property : null;
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }
    }

    private static Func<T, object?> Getter<T>(PropertyInfo member)
    {
        var it = Expression.Parameter(typeof(T), "it");
        return Expression.Lambda<Func<T, object?>>(Expression.Convert(Expression.Property(it, member), typeof(object)), it).Compile();
    }

    // a value as the database's cast to text writes it; enums by number, as stored by default (FacetDtos names them either way)
    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => text,
        Enum member => member.ToString("D"),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    // the builder's TopDistinct: the attribute's, else its default
    private static int? Top(Type entity, FacetGroup group) =>
        entity.GetProperty(group.Field)?.GetCustomAttributes<ProvideFacetAttribute>(true).OrderBy(f => f.Order).LastOrDefault() is { TopDistinct: > 0 } facet
            ? facet.TopDistinct
            : FacetLiterals.Options().DefaultTopDistinct;

    private static string Dynamic(string path) => path.Replace('/', '.');

    // the builder's options of counted values: the value as text (booleans by their coerced value), the label or the value, an OData equality
    private static void Fill(IEnumerable<FacetGroup> groups, IEnumerable<Counted> counted)
    {
        var byKey = groups.ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var row in counted)
        {
            var group = byKey[row.Group];
            var type = Type.GetType(group.ValueClrType ?? "") ?? typeof(string);
            var value = Coerce(row.Value, type);
            var display = (value is bool ? value : row.Value)?.ToString() ?? "";
            group.Options.Add(new FacetOption
            {
                Value = display,
                Label = row.Label ?? display,
                Count = row.Count,
                Enabled = true,
                OData = $"{group.ValuePath} eq {DefaultODataLiteralFormatter.ToSimpleLiteral(value, type)}",
            });
        }
    }

    // Nextended's FacetBuilder.Coerce: the text a database gave back as the value's type
    private static object? Coerce(object? raw, Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (raw is null || raw.GetType() == type)
        {
            return raw;
        }

        var text = raw.ToString() ?? "";
        try
        {
            if (type == typeof(Guid))
            {
                return Guid.Parse(text);
            }

            if (type == typeof(bool) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number != 0;
            }

            return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
        }
        catch
        {
            return text;
        }
    }

    private sealed record Counted
    {
        public string Group { get; set; } = "";

        public string Value { get; set; } = "";

        public string? Label { get; set; }

        public int Count { get; set; }
    }
}
