namespace Coworkee.Contracts.Settings;

/// <summary>A service of the app with its status as the server sees it; Source names where it comes from ("app" = Coworkee:Services, "aspireui" = an AspireUI), Urls lists every address when there are several, Description what its health check says (empty Url: a service without address, e.g. "antivirus").</summary>
public sealed record ServiceDto(string Name, string Title, string Url, ServiceHealth Health, string Source = "app", IReadOnlyList<string>? Urls = null, string? Description = null);
