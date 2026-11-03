using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<DateBucket>))]
public enum DateBucket
{
    None,
    Day,
    Week,
    Month,
    Quarter,
    Year,
}
