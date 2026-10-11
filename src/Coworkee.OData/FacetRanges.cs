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
        if (await values.MinAsync(cancellationToken) is not { } min || await values.MaxAsync(cancellationToken) is not { } max)
        {
            return group with { Options = [] };
        }

        var whole = type != typeof(decimal) && type != typeof(double) && type != typeof(float);
        // round widths up to cents, so bucket bounds read like prices instead of 28 digits
        var width = whole ? Math.Max(1, Math.Ceiling((max - min + 1) / buckets)) : Math.Max(0.01m, Math.Ceiling((max - min) / buckets * 100) / 100);
        var counts = await values.Where(v => v != null).GroupBy(v => Math.Floor((v!.Value - min) / width)).Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var path = group.ValuePath ?? group.Field;
        var last = (int)Math.Min(buckets - 1, Math.Floor((max - min) / width));
        var options = Enumerable.Range(0, last + 1).Select(i =>
        {
            var (from, to) = (min + (i * width), min + ((i + 1) * width));
            var count = counts.Where(c => i == last ? c.Key >= i : c.Key == i).Sum(c => c.Count);
            var upper = i == last ? $"{path} le {Text(max)}" : $"{path} lt {Text(to)}";
            return Bucket($"({path} ge {Text(from)} and {upper})", Text(from), Text(i == last ? max : to), count, "number");
        });
        return group with { Options = [.. options], Min = Text(min), Max = Text(max) };
    }

    private static async Task<FacetGroupDto> DatesAsync<T, TValue>(FacetGroupDto group, IQueryable<T> rows, ParameterExpression it, Expression member, CancellationToken cancellationToken)
        where TValue : struct
    {
        var values = rows.Select(Expression.Lambda<Func<T, TValue?>>(member.Type == typeof(TValue?) ? member : Expression.Convert(member, typeof(TValue?)), it));
        if (await values.MinAsync(cancellationToken) is not { } min || await values.MaxAsync(cancellationToken) is not { } max)
        {
            return group with { Options = [] };
        }

        var (first, lastDay) = (Day(min), Day(max));
        var unit = (lastDay - first).TotalDays <= 45 ? "day" : (lastDay - first).TotalDays <= 3 * 366 ? "month" : "year";
        var value = Expression.Parameter(typeof(TValue?), "v");
        var date = Expression.Property(value, nameof(Nullable<int>.Value));
        Expression key = Expression.Multiply(Expression.Property(date, "Year"), Expression.Constant(10000));
        key = unit == "year" ? key : Expression.Add(key, Expression.Multiply(Expression.Property(date, "Month"), Expression.Constant(100)));
        key = unit == "day" ? Expression.Add(key, Expression.Property(date, "Day")) : key;
        var counts = await values.Where(Expression.Lambda<Func<TValue?, bool>>(Expression.NotEqual(value, Expression.Constant(null, typeof(TValue?))), value))
            .GroupBy(Expression.Lambda<Func<TValue?, int>>(key, value))
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var path = group.ValuePath ?? group.Field;
        var dateOnly = typeof(TValue) == typeof(DateOnly);
        var start = unit == "day" ? first : unit == "month" ? new DateTime(first.Year, first.Month, 1) : new DateTime(first.Year, 1, 1);
        var options = new List<FacetOptionDto>();
        for (var from = start; from <= lastDay; from = Next(from, unit))
        {
            var to = Next(from, unit);
            var bucket = from.Year * 10000 + (unit == "year" ? 0 : from.Month * 100) + (unit == "day" ? from.Day : 0);
            var count = counts.FirstOrDefault(c => c.Key == bucket)?.Count ?? 0;
            options.Add(Bucket($"({path} ge {Literal(from, dateOnly)} and {path} lt {Literal(to, dateOnly)})", Iso(from), Iso(to), count, unit));
        }

        return group with { Options = options, Min = Iso(first), Max = Iso(lastDay) };
    }

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
