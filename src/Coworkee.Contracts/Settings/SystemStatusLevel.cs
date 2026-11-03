using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SystemStatusLevel>))]
public enum SystemStatusLevel
{
    Info,
    Ok,
    Warning,
    Error,
}
