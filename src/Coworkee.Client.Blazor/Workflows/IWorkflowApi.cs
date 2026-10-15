using Coworkee.Contracts.Workflows;

namespace Coworkee.Client.Blazor.Workflows;

public interface IWorkflowApi
{
    Task<WorkflowStatusDto> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<WorkflowStatusDto> RunAsync(string entityType, Guid entityId, string transition, string? comment = null, CancellationToken cancellationToken = default);

    Task<WorkflowStatusDto> DecideAsync(Guid approvalId, bool approve, string? comment = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalDto>> GetMyApprovalsAsync(CancellationToken cancellationToken = default);
}
