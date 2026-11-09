using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Sessions;

[RequiresPermission(IdentityPermissions.Sessions.View)]
public sealed record GetUserSessions(Guid UserId) : IQuery<Result<UserSessionsDto>>;
