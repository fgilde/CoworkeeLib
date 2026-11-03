using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;

namespace Coworkee.Workflows;

[RequiresPermission(WorkflowPermissions.Transition)]
[Description("Runs a transition of an entity's workflow, e.g. Submit; the transition's guards decide whether the user may.")]
public sealed record RunTransitionCommand(string EntityType, Guid EntityId, string Transition, string? Comment = null) : ICommand<Result<WorkflowStatusDto>>;
