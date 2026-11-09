using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Sessions;

/// <summary>How many active sessions each of the users has; users out of reach and users without a session are left out.</summary>
[RequiresPermission(IdentityPermissions.Sessions.View)]
public sealed record GetSessionCounts(IReadOnlyList<Guid> UserIds) : IQuery<Result<IReadOnlyDictionary<Guid, int>>>;
