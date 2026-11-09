using System.Globalization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Auditing;

/// <summary>OData conditions of a period picked in local days.</summary>
internal static class LogPeriod
{
    public static string? From(DateRange? range, string property) => range?.Start is { } from ? $"{property} ge {Literal(from.Date)}" : null;

    public static string? To(DateRange? range, string property) => range?.End is { } to ? $"{property} lt {Literal(to.Date.AddDays(1))}" : null;

    private static string Literal(DateTime localDay) =>
        new DateTimeOffset(DateTime.SpecifyKind(localDay, DateTimeKind.Local)).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
