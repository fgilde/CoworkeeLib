using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ServiceHealth>))]
public enum ServiceHealth
{
    Healthy,
    Unhealthy,
    Unreachable,
}

/// <summary>A service of the app (Coworkee:Services) with its status as the server sees it.</summary>
public sealed record ServiceDto(string Name, string Title, string Url, ServiceHealth Health);
