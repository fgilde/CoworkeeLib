using System.Text.Json.Serialization;

namespace Coworkee.Client.Blazor.Components.Query;

[JsonConverter(typeof(JsonStringEnumConverter<QueryLogic>))]
public enum QueryLogic
{
    And,
    Or,
}
