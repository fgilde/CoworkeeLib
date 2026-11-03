using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;

namespace Coworkee.Workflows;

[RequiresPermission(WorkflowPermissions.View)]
[Description("Reads the workflow states of several entities of one type, with the transitions the user may run and the open approval, without the history.")]
public sealed record GetWorkflowStatesQuery(string EntityType, IReadOnlyList<Guid> Ids) : IQuery<Result<IReadOnlyList<WorkflowStatusDto>>>;
