using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>Makes exactly these users the owners of the group; users who are not members yet join it.</summary>
[RequiresPermission(IdentityPermissions.Groups.ManageMembers)]
public sealed record SetGroupOwners(Guid Id, IReadOnlyList<Guid> UserIds) : ICommand<Result>;
