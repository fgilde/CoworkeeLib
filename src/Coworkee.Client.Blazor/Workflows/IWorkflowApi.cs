using Coworkee.Contracts.Workflows;

namespace Coworkee.Client.Blazor.Workflows;

public interface IWorkflowApi
{
    Task<WorkflowStatusDto> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>The states of several rows of one type in one request, without history; rows the user cannot see are missing.</summary>
    Task<IReadOnlyList<WorkflowStatusDto>> GetStatesAsync(string entityType, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default);

    Task<WorkflowStatusDto> RunAsync(string entityType, Guid entityId, string transition, string? comment = null, CancellationToken cancellationToken = default);

    Task<WorkflowStatusDto> DecideAsync(Guid approvalId, bool approve, string? comment = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalDto>> GetMyApprovalsAsync(CancellationToken cancellationToken = default);
}
