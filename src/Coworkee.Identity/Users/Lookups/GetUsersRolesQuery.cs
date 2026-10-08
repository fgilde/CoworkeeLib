using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Lookups;

[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUsersRolesQuery(IReadOnlyList<Guid> Ids) : IQuery<Result<IReadOnlyDictionary<Guid, IReadOnlyList<RoleRefDto>>>>;
