using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<NumberFormat>))]
public enum NumberFormat
{
    Number,
    Integer,
    Compact,
    Currency,
    Percent,
}
