namespace Coworkee.Application.Retention;

/// <summary>A kind of data that grows without end; the Retention module deletes what is older than the days set for <see cref="Key"/>, across all tenants.</summary>
public interface IRetentionTarget
{
    string Key { get; }

    /// <summary>English text, also the localization key.</summary>
    string Title { get; }

    string? Description { get; }

    /// <summary>The days kept when nothing is set; null keeps everything.</summary>
    int? DefaultDays { get; }

    Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken);

    /// <summary>Deletes in batches and returns how many rows went.</summary>
    Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken);
}
