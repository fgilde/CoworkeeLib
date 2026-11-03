using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Roles;

// an impersonator would otherwise pick a role of the target that holds more than he does
[NotWhileImpersonating]
public sealed record SetActiveRole(Guid? RoleId) : ICommand<Result>;
