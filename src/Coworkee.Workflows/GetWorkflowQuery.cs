using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;

namespace Coworkee.Workflows;

[RequiresPermission(WorkflowPermissions.View)]
[Description("Reads the workflow state of an entity, the transitions the user may run now, the open approval and the history.")]
public sealed record GetWorkflowQuery(string EntityType, Guid EntityId) : IQuery<Result<WorkflowStatusDto>>;
