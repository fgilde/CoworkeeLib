using Coworkee.Domain;

namespace Coworkee.Retention;

/// <summary>The last run of a target and whether one is going on.</summary>
[NotAudited]
public sealed class RetentionRun
{
    public required string Key { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public long? LastDeleted { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? RunningSince { get; set; }

    /// <summary>Who started the last run by hand; null for the nightly job.</summary>
    public Guid? LastRunBy { get; set; }
}
