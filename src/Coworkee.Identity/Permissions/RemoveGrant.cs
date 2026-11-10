using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

/// <summary>Revokes one granted permission; one that is not granted is no error.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record RemoveGrant(PermissionProviderType ProviderType, Guid ProviderKey, string Name) : ICommand<Result>;
