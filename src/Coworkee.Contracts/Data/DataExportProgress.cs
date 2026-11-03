namespace Coworkee.Contracts.Data;

/// <summary>
/// One line of the export stream (application/x-ndjson): <paramref name="Phase"/> is counting, reading, writing, ready, failed or cancelled.
/// The ready line carries the anonymous one-time <paramref name="Url"/> (relative to the API, five minutes) of the file; every line carries the <paramref name="JobId"/>.
/// </summary>
public sealed record DataExportProgress(string Phase, int Percent, string? Url = null, string? FileName = null, Guid? JobId = null, string? Message = null)
{
    /// <summary>The rows read so far (reading and writing lines).</summary>
    public int? Rows { get; init; }

    /// <summary>The rows the export holds, once they are counted.</summary>
    public int? Total { get; init; }

    public const string Counting = "counting";
    public const string Reading = "reading";
    public const string Writing = "writing";
    public const string Ready = "ready";
    public const string Failed = "failed";

    /// <summary>The user cancelled the export (<c>POST /api/v1/data/jobs/{id}/cancel</c>): no file, no mail.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// The last line of a stream with more rows than a request takes on servers before 0.0.1.50 (<c>JobId</c>: the export job). The stream now follows that
    /// job with reading, writing and ready lines and no longer sends it.
    /// </summary>
    public const string Queued = "queued";
}
