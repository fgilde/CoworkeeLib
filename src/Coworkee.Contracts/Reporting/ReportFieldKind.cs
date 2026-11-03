using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<ReportFieldKind>))]
public enum ReportFieldKind
{
    Text,
    Integer,
    Number,
    Date,
    Boolean,
    Enum,
    Guid,
}
