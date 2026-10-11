using System.Globalization;
using System.Linq.Expressions;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Querying;

/// <summary>Buckets dates into numbers the database computes from date parts, e.g. 202603 for March 2026; weeks start on Monday.</summary>
/// <remarks>ponytail: buckets follow the stored time (UTC for DateTimeOffset); a per-user time zone needs a shifted date before bucketing.</remarks>
internal static class DateBuckets
{
    /// <summary>The Monday weeks are counted from where the database has no day of week (<see cref="ReportGroup.WeekIndex"/>).</summary>
    public static readonly DateOnly Epoch = new(2000, 1, 3);

    public static Expression Key(Expression date, DateBucket bucket, bool weekIndex = false)
    {
        if (Nullable.GetUnderlyingType(date.Type) is not null)
        {
            var value = Key(Expression.Property(date, nameof(Nullable<int>.Value)), bucket, weekIndex);
            return Expression.Condition(Expression.Property(date, nameof(Nullable<int>.HasValue)),
                Expression.Convert(value, typeof(int?)), Expression.Constant(null, typeof(int?)));
        }

        if (weekIndex)
        {
            return Expression.Divide(DaysSinceEpoch(date), Expression.Constant(7));
        }

        return bucket switch
        {
            DateBucket.Year => Part(date, "Year"),
            DateBucket.Quarter => Add(Times(Part(date, "Year"), 10), Divide(Add(Part(date, "Month"), Expression.Constant(2)), 3)),
            DateBucket.Month => Add(Times(Part(date, "Year"), 100), Part(date, "Month")),
            DateBucket.Week => Day(Monday(date)),
            _ => Day(date),
        };
    }

    /// <summary>The first day of the bucket and the first day after it.</summary>
    public static (DateOnly From, DateOnly To) Range(int key, DateBucket bucket) => bucket switch
    {
        DateBucket.Year => (new DateOnly(key, 1, 1), new DateOnly(key + 1, 1, 1)),
        DateBucket.Quarter => (new DateOnly(key / 10, ((key % 10) - 1) * 3 + 1, 1), new DateOnly(key / 10, ((key % 10) - 1) * 3 + 1, 1).AddMonths(3)),
        DateBucket.Month => (new DateOnly(key / 100, key % 100, 1), new DateOnly(key / 100, key % 100, 1).AddMonths(1)),
        DateBucket.Week => (FromDay(key), FromDay(key).AddDays(7)),
        _ => (FromDay(key), FromDay(key).AddDays(1)),
    };

    public static string Label(int key, DateBucket bucket) => bucket switch
    {
        DateBucket.Year => key.ToString(CultureInfo.InvariantCulture),
        DateBucket.Quarter => $"{key / 10} Q{key % 10}",
        DateBucket.Month => $"{key / 100}-{key % 100:00}",
        DateBucket.Week => $"{ISOWeek.GetYear(FromDay(key).ToDateTime(TimeOnly.MinValue))}-W{ISOWeek.GetWeekOfYear(FromDay(key).ToDateTime(TimeOnly.MinValue)):00}",
        _ => FromDay(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    /// <summary>The day key (yyyymmdd) of the Monday of a week index.</summary>
    public static int DayOfWeekIndex(int index)
    {
        var monday = Epoch.AddDays(index * 7);
        return monday.Year * 10000 + monday.Month * 100 + monday.Day;
    }

    private static Expression DaysSinceEpoch(Expression date)
    {
        Expression epoch = date.Type == typeof(DateTimeOffset) ? Expression.Constant(new DateTimeOffset(Epoch.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            : date.Type == typeof(DateOnly) ? Expression.Constant(Epoch)
            : Expression.Constant(Epoch.ToDateTime(TimeOnly.MinValue));
        var method = typeof(Microsoft.EntityFrameworkCore.SqlServerDbFunctionsExtensions).GetMethod("DateDiffDay", [typeof(Microsoft.EntityFrameworkCore.DbFunctions), date.Type, date.Type])
            ?? throw new NotSupportedException($"SQL Server cannot count days of {date.Type.Name}.");
        return Expression.Call(method, Expression.Constant(Microsoft.EntityFrameworkCore.EF.Functions), epoch, date);
    }

    private static DateOnly FromDay(int key) => new(key / 10000, key / 100 % 100, key % 100);

    private static Expression Day(Expression date) =>
        Add(Add(Times(Part(date, "Year"), 10000), Times(Part(date, "Month"), 100)), Part(date, "Day"));

    private static Expression Monday(Expression date)
    {
        var sinceMonday = Expression.Modulo(Expression.Add(Expression.Convert(Expression.Property(date, "DayOfWeek"), typeof(int)), Expression.Constant(6)), Expression.Constant(7));
        var back = Expression.Negate(sinceMonday);
        return date.Type == typeof(DateOnly)
            ? Expression.Call(date, nameof(DateOnly.AddDays), null, back)
            : Expression.Call(date, nameof(DateTime.AddDays), null, Expression.Convert(back, typeof(double)));
    }

    private static Expression Part(Expression date, string name) => Expression.Property(date, name);

    private static Expression Add(Expression left, Expression right) => Expression.Add(left, right);

    private static Expression Times(Expression value, int factor) => Expression.Multiply(value, Expression.Constant(factor));

    private static Expression Divide(Expression value, int divisor) => Expression.Divide(value, Expression.Constant(divisor));
}
