using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>Makes the group the one new users join; null leaves the tenant without a default group.</summary>
[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record SetDefaultGroup(Guid? GroupId) : ICommand<Result>;
