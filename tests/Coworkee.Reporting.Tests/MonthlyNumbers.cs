using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Coworkee.OData;

namespace Coworkee.Reporting.Tests;

public enum Mood
{
    Calm,
    [Display(Name = "Very busy")]
    Busy,
}

/// <summary>A computed row like an app's headcount per month: no table, no key.</summary>
public sealed record MonthlyNumber(
    DateOnly Month,
    string Team,
    Mood Mood,
    [property: DisplayName("Head count")] int Headcount,
    decimal Rate);

public sealed class SaleRow
{
    public string Region { get; init; } = string.Empty;

    public DateTimeOffset SoldAt { get; init; }

    public decimal Amount { get; init; }
}

/// <summary>Two teams over 2025; remembers the last context, so tests see the date range a widget passed.</summary>
public static class MonthlyNumbers
{
    public const string ViewNumbers = "Numbers.View";

    public static ReportSourceContext? Last { get; private set; }

    public static IReadOnlyList<MonthlyNumber> All { get; } =
    [
        .. Enumerable.Range(0, 12).SelectMany(m => new[]
        {
            new MonthlyNumber(new DateOnly(2025, 1, 1).AddMonths(m), "North", m % 3 == 0 ? Mood.Busy : Mood.Calm, 10 + m, 0.5m + (m / 100m)),
            new MonthlyNumber(new DateOnly(2025, 1, 1).AddMonths(m), "South", Mood.Calm, 20 - m, 1m),
        }),
    ];

    public static Task<IEnumerable<MonthlyNumber>> RowsAsync(ReportSourceContext context, CancellationToken cancellationToken)
    {
        Last = context;
        return Task.FromResult(All.Where(r => (context.DateFrom is not { } from || r.Month >= from) && (context.DateTo is not { } to || r.Month < to)));
    }

    /// <summary>A source over the database that keeps the set's tenant and row filters.</summary>
    public static async Task<IEnumerable<SaleRow>> SalesAsync(ReportSourceContext context, CancellationToken cancellationToken) =>
        (await ODataEntityAccess.VisibleAsync<Sale>(context.Services, cancellationToken))
            .Select(s => new SaleRow { Region = s.Region, SoldAt = s.SoldAt, Amount = s.Amount });
}
