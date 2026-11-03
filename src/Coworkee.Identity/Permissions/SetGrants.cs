using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

[Coworkee.Application.Messaging.AiTool(Exclude = true)]
[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record SetGrants(PermissionProviderType ProviderType, Guid ProviderKey, IReadOnlyList<string> Names) : ICommand<Result>;
