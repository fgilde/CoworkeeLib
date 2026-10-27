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

    /// <param name="binary">A DateTimeOffset the database keeps as a number (<see cref="ReportGroup.BinaryDate"/>).</param>
    public static Expression Key(Expression date, DateBucket bucket, bool weekIndex = false, bool binary = false)
    {
        if (Nullable.GetUnderlyingType(date.Type) is not null)
        {
            var value = Key(Expression.Property(date, nameof(Nullable<int>.Value)), bucket, weekIndex, binary);
            return Expression.Condition(Expression.Property(date, nameof(Nullable<int>.HasValue)),
                Expression.Convert(value, typeof(int?)), Expression.Constant(null, typeof(int?)));
        }

        if (weekIndex)
        {
            return Expression.Divide(DaysSinceEpoch(date), Expression.Constant(7));
        }

        if (binary)
        {
            return Expression.Convert(FromStored(date, bucket), typeof(int));
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

    /// <summary>The stored number of a DateTimeOffset, as SQLite's abs() returns it (the number is never negative); mapped in the model, only for queries.</summary>
    public static long Stored(DateTimeOffset value) => throw new InvalidOperationException("Stored runs only inside a database query.");

    // DateTimeOffsetToBinaryConverter stores (local ticks / 1000) << 11 | offset minutes (11 bits, two's complement); the key is computed in UTC
    // with integer arithmetic, days from 0001-01-01 (a Monday) to a civil date as in H. Hinnant's days_from_civil inverse
    private static Expression FromStored(Expression date, DateBucket bucket)
    {
        var stored = Expression.Call(typeof(DateBuckets).GetMethod(nameof(Stored))!, date);
        var offsetMinutes = Sub(Mod(Add(Mod(stored, 2048), C(1024)), 2048), C(1024));
        var utcUnits = Sub(Div(stored, 2048), Mul(offsetMinutes, 600_000));
        var days = Div(utcUnits, 864_000_000);
        if (bucket == DateBucket.Week)
        {
            days = Sub(days, Mod(days, 7));
        }

        // days since 1970-03-01 shifted to 0000-03-01: z = days(0001-01-01) - 719162 + 719468
        var z = Add(days, C(306));
        var era = Div(z, 146_097);
        var doe = Sub(z, Mul(era, 146_097));
        var yoe = Div(Sub(Add(Sub(doe, Div(doe, 1460)), Div(doe, 36_524)), Div(doe, 146_096)), 365);
        var doy = Sub(doe, Sub(Add(Mul(yoe, 365), Div(yoe, 4)), Div(yoe, 100)));
        var mp = Div(Add(Mul(doy, 5), C(2)), 153);
        var day = Add(Sub(doy, Div(Add(Mul(mp, 153), C(2)), 5)), C(1));
        var month = Expression.Condition(Expression.LessThan(mp, C(10)), Add(mp, C(3)), Sub(mp, C(9)));
        var year = Add(Add(Mul(era, 400), yoe), Expression.Condition(Expression.LessThanOrEqual(month, C(2)), C(1), C(0)));
        return bucket switch
        {
            DateBucket.Year => year,
            DateBucket.Quarter => Add(Mul(year, 10), Div(Add(month, C(2)), 3)),
            DateBucket.Month => Add(Mul(year, 100), month),
            _ => Add(Add(Mul(year, 10_000), Mul(month, 100)), day),
        };

        static Expression C(long value) => Expression.Constant(value);
        static Expression Add(Expression a, Expression b) => Expression.Add(a, b);
        static Expression Sub(Expression a, Expression b) => Expression.Subtract(a, b);
        static Expression Mul(Expression a, long b) => Expression.Multiply(a, C(b));
        static Expression Div(Expression a, long b) => Expression.Divide(a, C(b));
        static Expression Mod(Expression a, long b) => Expression.Modulo(a, C(b));
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
