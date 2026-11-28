using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>
/// The caller's own top groups under group-scoped administration (<see cref="GroupDto.Locked"/>): he does not move, delete or give them roles, and
/// their data filters are read-only for him. Empty for tenant-wide callers.
/// </summary>
[RequiresPermission(IdentityPermissions.Groups.View)]
public sealed record GetLockedGroups : IQuery<Result<IReadOnlyList<Guid>>>;
