namespace Coworkee.Contracts.Configuration;

public sealed class CoworkeeServiceOptions
{
    public string? Url { get; set; }

    /// <summary>The path probed for the live status, e.g. "/health"; without it any answer below 500 counts as running.</summary>
    public string? HealthPath { get; set; }

    public string? Title { get; set; }
}
