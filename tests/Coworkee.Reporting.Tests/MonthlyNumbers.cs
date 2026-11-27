using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Coworkee.OData;
using Microsoft.Extensions.DependencyInjection;

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

/// <summary>A source whose amounts are in the currency of the signed-in user.</summary>
public sealed record CostRow(string Code, decimal Amount);

public static class CostRows
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> Currencies = new();

    public static Task<IEnumerable<CostRow>> RowsAsync(ReportSourceContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<CostRow>>([new("a", 500), new("b", 400), new("c", 600)]);

    public static Task<string?> CurrencyAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        Task.FromResult(services.GetRequiredService<Coworkee.Core.Security.ICurrentUser>().UserId is { } id && Currencies.TryGetValue(id, out var code) ? code : null);
}
