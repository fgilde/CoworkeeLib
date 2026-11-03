using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>Creates the group at its path or, when a group of that name already sits there, updates its description.</summary>
[RequiresPermission(IdentityPermissions.Groups.Manage)]
[AiTool(Exclude = true)]
public sealed record ImportGroup(GroupImportRow Row) : ICommand<Result<Guid>>;
