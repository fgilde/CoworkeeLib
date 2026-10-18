namespace Coworkee.Contracts.Retention;

/// <summary>A kind of data with its days to keep (null: kept forever), what those days would delete now and the last run.</summary>
public sealed record RetentionTargetDto(
    string Key,
    string Title,
    string? Description,
    int? Days,
    int? DefaultDays,
    long? Affected,
    DateTimeOffset? LastRunAt,
    long? LastDeleted,
    string? LastError,
    bool Running);
