using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;

namespace Coworkee.Workflows;

[RequiresPermission(WorkflowPermissions.View)]
[Description("Lists the open approvals waiting for the user's decision, oldest first.")]
public sealed record GetMyApprovalsQuery : IQuery<Result<IReadOnlyList<ApprovalDto>>>;
