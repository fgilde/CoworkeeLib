using System.Text.Json.Serialization;

namespace Coworkee.Client.Blazor.Components.Query;

[JsonConverter(typeof(JsonStringEnumConverter<QueryFieldType>))]
public enum QueryFieldType
{
    Text,
    Number,
    Boolean,

    /// <summary>A day without time (<c>DateOnly</c>).</summary>
    Date,

    /// <summary>A point in time, compared by whole days in the user's offset.</summary>
    DateTime,
    Enum,
    Guid,
}
