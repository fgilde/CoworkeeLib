using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<DateRangePreset>))]
public enum DateRangePreset
{
    Today,
    Last7Days,
    Last30Days,
    Last90Days,
    ThisMonth,
    LastMonth,
    ThisQuarter,
    ThisYear,
    LastYear,
    Last12Months,
    Custom,
}
