using Coworkee.Contracts.Retention;

namespace Coworkee.Client.Blazor.Retention;

/// <summary>A row of the retention page: the target as loaded and the days being edited with their live count.</summary>
public sealed class RetentionRow(RetentionTargetDto target)
{
    public RetentionTargetDto Target { get; } = target;

    public int? Days { get; set; } = target.Days;

    public long? Affected { get; set; } = target.Affected;

    public bool Counting { get; set; }

    internal CancellationTokenSource? PendingCount { get; set; }

    public bool Changed => (Days is > 0 ? Days : null) != Target.Days;
}
