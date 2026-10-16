using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Workflows;

namespace Coworkee.Client.Blazor.Workflows;

internal sealed class WorkflowApi(HttpClient http) : ApiClientBase(http), IWorkflowApi
{
    public Task<WorkflowStatusDto> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
        GetAsync<WorkflowStatusDto>(Url(entityType, entityId), cancellationToken);

    public async Task<IReadOnlyList<WorkflowStatusDto>> GetStatesAsync(string entityType, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default) =>
        await SendAsync<WorkflowStatusDto[]>(HttpMethod.Post, $"api/v1/workflows/{Uri.EscapeDataString(entityType)}/states", new WorkflowStatesRequest(entityIds), cancellationToken);

    public Task<WorkflowStatusDto> RunAsync(string entityType, Guid entityId, string transition, string? comment = null, CancellationToken cancellationToken = default) =>
        SendAsync<WorkflowStatusDto>(HttpMethod.Post, $"{Url(entityType, entityId)}/{Uri.EscapeDataString(transition)}", new TransitionRequest(comment), cancellationToken);

    public Task<WorkflowStatusDto> DecideAsync(Guid approvalId, bool approve, string? comment = null, CancellationToken cancellationToken = default) =>
        SendAsync<WorkflowStatusDto>(HttpMethod.Post, $"api/v1/workflows/approvals/{approvalId}", new DecisionRequest(approve, comment), cancellationToken);

    public async Task<IReadOnlyList<ApprovalDto>> GetMyApprovalsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ApprovalDto[]>("api/v1/workflows/approvals", cancellationToken);

    private static string Url(string entityType, Guid entityId) => $"api/v1/workflows/{Uri.EscapeDataString(entityType)}/{entityId}";
}
