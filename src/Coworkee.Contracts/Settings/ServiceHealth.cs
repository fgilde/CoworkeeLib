using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ServiceHealth>))]
public enum ServiceHealth
{
    Healthy,
    Unhealthy,
    Unreachable,
    Unknown,
}
