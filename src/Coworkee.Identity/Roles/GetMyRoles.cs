using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Roles;

public sealed record GetMyRoles(string? Permission) : IQuery<Result<MyRolesDto>>;
