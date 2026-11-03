using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

[RequiresPermission(IdentityPermissions.Groups.ManageMembers)]
public sealed record AddGroupMembers(Guid Id, IReadOnlyList<Guid> UserIds) : ICommand<Result>;
