using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;

namespace Coworkee.Workflows;

[RequiresPermission(WorkflowPermissions.View)]
[Description("Approves or rejects an open approval the user is an approver of.")]
public sealed record DecideApprovalCommand(Guid ApprovalId, bool Approve, string? Comment = null) : ICommand<Result<WorkflowStatusDto>>;
