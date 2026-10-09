using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Admin;

/// <summary>Makes a user member of exactly these groups of the organisation.</summary>
[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record SetUserGroups(Guid Id, IReadOnlyList<Guid> GroupIds) : ICommand<Result>;
