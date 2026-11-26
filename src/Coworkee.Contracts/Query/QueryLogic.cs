using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Query;

[JsonConverter(typeof(JsonStringEnumConverter<QueryLogic>))]
public enum QueryLogic
{
    And,
    Or,
}
