using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

/// <summary>Who loses the custom permission <paramref name="Name"/> when it is deleted.</summary>
[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record GetCustomPermissionUsage(string Name) : IQuery<Result<CustomPermissionUsageDto>>;
