using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ServiceHealth>))]
public enum ServiceHealth
{
    Healthy,
    Unhealthy,
    Unreachable,
}

/// <summary>A service of the app with its status as the server sees it; Source names where it comes from ("app" = Coworkee:Services, "aspireui" = an AspireUI), Urls lists every address when there are several.</summary>
public sealed record ServiceDto(string Name, string Title, string Url, ServiceHealth Health, string Source = "app", IReadOnlyList<string>? Urls = null);
