namespace Coworkee.Storage;

/// <summary>
/// Checks an upload for malware before it is stored: the files, record attachments, chat files and registration documents call it, and app features
/// that take files should too. The default (<see cref="ClamAvVirusScanner"/>) asks clamd at <c>ConnectionStrings:antivirus</c> and lets every file pass
/// when that is not set. Register your own to scan elsewhere; the last registration wins.
/// </summary>
public interface IVirusScanner
{
    /// <param name="content">Read from its current position to the end; it may be left anywhere.</param>
    /// <param name="fileName">For the logs and the refusal; the name the user gave.</param>
    Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken);
}

public enum VirusScanOutcome
{
    /// <summary>Scanned, nothing found.</summary>
    Clean,

    /// <summary>No scanner is configured: the file is stored unscanned.</summary>
    NotScanned,

    /// <summary>Malware found; <see cref="VirusScanResult.Detail"/> names it.</summary>
    Infected,

    /// <summary>A scanner is configured but gave no verdict (unreachable, error, unknown answer); <see cref="VirusScanResult.Detail"/> says why, for the log.</summary>
    Failed,
}

/// <summary>The verdict of an <see cref="IVirusScanner"/>. Infected and failed files are refused.</summary>
public sealed record VirusScanResult(VirusScanOutcome Outcome, string? Detail = null)
{
    public static VirusScanResult Clean { get; } = new(VirusScanOutcome.Clean);

    public static VirusScanResult NotScanned { get; } = new(VirusScanOutcome.NotScanned);

    public static VirusScanResult Infected(string finding) => new(VirusScanOutcome.Infected, finding);

    public static VirusScanResult Failed(string reason) => new(VirusScanOutcome.Failed, reason);

    public bool Refused => Outcome is VirusScanOutcome.Infected or VirusScanOutcome.Failed;

    /// <summary>Why <paramref name="fileName"/> is refused, for the person who uploaded it; null when it may be stored.</summary>
    public string? RefusalFor(string fileName) => Outcome switch
    {
        VirusScanOutcome.Infected => $"The file '{fileName}' was refused: the virus scan found {Detail}.",
        VirusScanOutcome.Failed => $"The file '{fileName}' could not be checked for viruses and was not stored. Try again in a few minutes.",
        _ => null,
    };
}
