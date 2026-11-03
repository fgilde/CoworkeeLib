using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<AggregateFunction>))]
public enum AggregateFunction
{
    Count,
    Sum,
    Average,
    Min,
    Max,
    DistinctCount,
}
