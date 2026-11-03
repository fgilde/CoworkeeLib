using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetEffectivePermissions(Guid UserId) : IQuery<Result<IReadOnlyList<string>>>;
