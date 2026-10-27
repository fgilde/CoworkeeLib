using Coworkee.Contracts.Data;
using Coworkee.Domain;

namespace Coworkee.OData.Transfer;

/// <summary>
/// An import or export of one user. Its owner follows the progress over the user's realtime topic: every saved change of the job
/// sends an EntityChanged event there.
/// </summary>
[NotAudited]
public sealed class DataTransferJob : Entity, IMultiTenant, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public DataTransferKind Kind { get; set; }

    public required string EntitySet { get; set; }

    public DataTransferStatus Status { get; set; }

    public bool DryRun { get; set; }

    /// <summary>Import: the ImportSettings, export: the query string and format, as JSON.</summary>
    public string? Settings { get; set; }

    public string? FileName { get; set; }

    // ponytail: uploads and results live in the job row; move them to blob storage if files grow beyond a few MB
    public byte[]? Input { get; set; }

    public byte[]? Result { get; set; }

    public string? ResultFileName { get; set; }

    public string? ResultContentType { get; set; }

    public int Total { get; set; }

    public int Processed { get; set; }

    public int Created { get; set; }

    public int Updated { get; set; }

    public int Failed { get; set; }

    /// <summary>The ids of the rows the import created, as JSON, for undo.</summary>
    public string? CreatedIds { get; set; }

    public string? Message { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it began running; the stale sweep measures from here, so time in the queue does not count.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];
}

/// <summary>A saved column mapping of an entity set, shared in the organisation.</summary>
public sealed class ImportProfile : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string EntitySet { get; set; }

    public required string Name { get; set; }

    /// <summary>Column header to field, as JSON.</summary>
    public required string Columns { get; set; }

    public ImportMode Mode { get; set; }

    public string? KeyField { get; set; }

    public string? Culture { get; set; }
}
