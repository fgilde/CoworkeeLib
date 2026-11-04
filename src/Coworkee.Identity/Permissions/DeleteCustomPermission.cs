using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

/// <summary>Deletes the custom permission <paramref name="Name"/> with its grants in every organisation.</summary>
[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record DeleteCustomPermission(string Name) : ICommand<Result>;
