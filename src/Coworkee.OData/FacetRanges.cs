using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData;

/// <summary>
/// The smallest and largest value of a number or date facet and a histogram of buckets, each an option with its own OData range,
/// so clients draw the distribution and client entities recount it locally. Numbers get equal buckets, dates calendar days, months or years.
/// </summary>
internal static class FacetRanges
{
    private static readonly MethodInfo DatesMethod = typeof(FacetRanges).GetMethod(nameof(DatesAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Task<FacetGroupDto> AddAsync<T>(FacetGroupDto group, IQueryable<T> rows, int buckets, CancellationToken cancellationToken)
    {
        var it = Expression.Parameter(typeof(T), "it");
        var member = (group.ValuePath ?? group.Field).Split('/', '.').Aggregate((Expression)it, Expression.Property);
        var type = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
        return group.ValueKind == FacetValueKind.Date
            ? (Task<FacetGroupDto>)DatesMethod.MakeGenericMethod(typeof(T), type).Invoke(null, [group, rows, it, member, cancellationToken])!
            : NumbersAsync(group, rows, it, member, type, Math.Clamp(buckets, 1, 50), cancellationToken);
    }

    private static async Task<FacetGroupDto> NumbersAsync<T>(FacetGroupDto group, IQueryable<T> rows, ParameterExpression it, Expression member, Type type, int buckets,
        CancellationToken cancellationToken)
    {
        var values = rows.Select(Expression.Lambda<Func<T, decimal?>>(Expression.Convert(member, typeof(decimal?)), it));
        if (await Run(values, () => values.MinAsync(cancellationToken), values.Min) is not { } min || await Run(values, () => values.MaxAsync(cancellationToken), values.Max) is not { } max)
        {
            return group with { Options = [] };
        }

        var whole = type != typeof(decimal) && type != typeof(double) && type != typeof(float);
        // round widths up to cents, so bucket bounds read like prices instead of 28 digits
        var width = whole ? Math.Max(1, Math.Ceiling((max - min + 1) / buckets)) : Math.Max(0.01m, Math.Ceiling((max - min) / buckets * 100) / 100);
        var path = group.ValuePath ?? group.Field;
        var last = (int)Math.Min(buckets - 1, Math.Floor((max - min) / width));
        var counts = await CountAsync(values, [.. Enumerable.Range(1, last).Select(i => min + (i * width))], cancellationToken);
        var options = Enumerable.Range(0, last + 1).Select(i =>
        {
            var (from, to) = (min + (i * width), min + ((i + 1) * width));
            var count = counts[i];
            var upper = i == last ? $"{path} le {Text(max)}" : $"{path} lt {Text(to)}";
            return Bucket($"({path} ge {Text(from)} and {upper})", Text(from), Text(i == last ? max : to), count, "number");
        });
        return group with { Options = [.. options], Min = Text(min), Max = Text(max) };
    }

    private static async Task<FacetGroupDto> DatesAsync<T, TValue>(FacetGroupDto group, IQueryable<T> rows, ParameterExpression it, Expression member, CancellationToken cancellationToken)
        where TValue : struct
    {
        var values = rows.Select(Expression.Lambda<Func<T, TValue?>>(member.Type == typeof(TValue?) ? member : Expression.Convert(member, typeof(TValue?)), it));
        if (await Run(values, () => values.MinAsync(cancellationToken), values.Min) is not { } min || await Run(values, () => values.MaxAsync(cancellationToken), values.Max) is not { } max)
        {
            return group with { Options = [] };
        }

        var (first, lastDay) = (Day(min), Day(max));
        var unit = (lastDay - first).TotalDays <= 45 ? "day" : (lastDay - first).TotalDays <= 3 * 366 ? "month" : "year";
        var path = group.ValuePath ?? group.Field;
        var dateOnly = typeof(TValue) == typeof(DateOnly);
        var start = unit == "day" ? first : unit == "month" ? new DateTime(first.Year, first.Month, 1) : new DateTime(first.Year, 1, 1);
        var starts = new List<DateTime>();
        for (var from = start; from <= lastDay; from = Next(from, unit))
        {
            starts.Add(from);
        }

        var counts = await CountAsync(values, [.. starts.Skip(1).Select(Value<TValue>)], cancellationToken);
        var options = starts.Select((from, i) =>
            Bucket($"({path} ge {Literal(from, dateOnly)} and {path} lt {Literal(Next(from, unit), dateOnly)})", Iso(from), Iso(Next(from, unit)), counts[i], unit)).ToList();

        return group with { Options = options, Min = Iso(first), Max = Iso(lastDay) };
    }

    // buckets by comparisons only (CASE WHEN v < b1 THEN 0 WHEN v < b2 THEN 1 ...): every provider translates them, unlike FLOOR or date parts
    private static async Task<int[]> CountAsync<TValue>(IQueryable<TValue?> values, IReadOnlyList<TValue> uppers, CancellationToken cancellationToken)
        where TValue : struct
    {
        var value = Expression.Parameter(typeof(TValue?), "v");
        Expression index = Expression.Constant(uppers.Count);
        for (var i = uppers.Count - 1; i >= 0; i--)
        {
            index = Expression.Condition(Expression.LessThan(value, Expression.Constant(uppers[i], typeof(TValue?))), Expression.Constant(i), index);
        }

        var grouped = values.Where(Expression.Lambda<Func<TValue?, bool>>(Expression.NotEqual(value, Expression.Constant(null, typeof(TValue?))), value))
            .GroupBy(Expression.Lambda<Func<TValue?, int>>(index, value))
            .Select(g => new { g.Key, Count = g.Count() });
        var counts = await Run(grouped, () => grouped.ToListAsync(cancellationToken), grouped.ToList);
        var result = new int[uppers.Count + 1];
        foreach (var count in counts)
        {
            result[count.Key] = count.Count;
        }

        return result;
    }

    // the rows of a bounded set are in memory (FacetCounts), which has no async queries
    private static Task<TResult> Run<TResult>(IQueryable rows, Func<Task<TResult>> database, Func<TResult> memory) =>
        rows.Provider is Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider ? database() : Task.FromResult(memory());

    private static TValue Value<TValue>(DateTime day) => (TValue)(object)(typeof(TValue) == typeof(DateTimeOffset) ? new DateTimeOffset(day, TimeSpan.Zero)
        : typeof(TValue) == typeof(DateOnly) ? DateOnly.FromDateTime(day) : DateTime.SpecifyKind(day, DateTimeKind.Utc));

    private static FacetOptionDto Bucket(string odata, string from, string to, int count, string unit) =>
        new(JsonSerializer.SerializeToElement(from), from, count > 0, count, odata) { Meta = new Dictionary<string, string> { ["from"] = from, ["to"] = to, ["unit"] = unit } };

    private static DateTime Day(object value) => value switch
    {
        DateTimeOffset offset => offset.UtcDateTime.Date,
        DateOnly day => day.ToDateTime(TimeOnly.MinValue),
        _ => ((DateTime)value).Date,
    };

    private static DateTime Next(DateTime from, string unit) => unit switch
    {
        "day" => from.AddDays(1),
        "month" => from.AddMonths(1),
        _ => from.AddYears(1),
    };

    private static string Literal(DateTime day, bool dateOnly) => dateOnly ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : day.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture);

    private static string Iso(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Text(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);
}
