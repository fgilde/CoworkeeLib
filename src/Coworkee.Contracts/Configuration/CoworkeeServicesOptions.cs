namespace Coworkee.Contracts.Configuration;

/// <summary>The services of the app by name (<c>Coworkee:Services:myapp-api:Url</c>); the app host fills it while developing, appsettings add or override entries.</summary>
public sealed class CoworkeeServicesOptions() : Dictionary<string, CoworkeeServiceOptions>(StringComparer.OrdinalIgnoreCase)
{
    public const string Section = "Coworkee:Services";
}

public sealed class CoworkeeServiceOptions
{
    public string? Url { get; set; }

    /// <summary>The path probed for the live status, e.g. "/health"; without it any answer below 500 counts as running.</summary>
    public string? HealthPath { get; set; }

    public string? Title { get; set; }
}
