namespace Coworkee.Application.Retention;

/// <summary>A target deleted <see cref="Deleted"/> rows but some could not go; the run records both.</summary>
public sealed class RetentionIncompleteException(long deleted, string message) : Exception(message)
{
    public long Deleted { get; } = deleted;
}
