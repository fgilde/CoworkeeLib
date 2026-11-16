using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ServiceHealth>))]
public enum ServiceHealth
{
    Healthy,
    Unhealthy,
    Unreachable,
    Unknown,

    /// <summary>Up, but not fully working yet or any more, e.g. ClamAV while it loads its signatures.</summary>
    Degraded,
}
