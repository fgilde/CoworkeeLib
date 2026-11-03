using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

[RequiresPermission(IdentityPermissions.Groups.ManageMembers)]
public sealed record MoveGroupMembers(Guid FromId, Guid ToId) : ICommand<Result>;
