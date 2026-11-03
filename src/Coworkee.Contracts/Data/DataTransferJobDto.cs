namespace Coworkee.Contracts.Data;

public sealed record DataTransferJobDto(
    Guid Id,
    DataTransferKind Kind,
    string EntitySet,
    DataTransferStatus Status,
    bool DryRun,
    int Total,
    int Processed,
    int Created,
    int Updated,
    int Failed,
    string? FileName,
    string? ResultFileName,
    string? Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    bool CanUndo)
{
    public bool IsFinished => Status is DataTransferStatus.Completed or DataTransferStatus.Failed or DataTransferStatus.Undone or DataTransferStatus.Cancelled;
}
