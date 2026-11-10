using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

/// <summary>Grants one permission to a role, group or user; the other grants stay, so two administrators editing at once do not undo each other.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record AddGrant(PermissionProviderType ProviderType, Guid ProviderKey, string Name) : ICommand<Result>;
