using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<ReportSortBy>))]
public enum ReportSortBy
{
    Key,
    Value,
}
