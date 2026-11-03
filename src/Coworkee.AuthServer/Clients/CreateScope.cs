using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.AuthServer.Clients;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record CreateScope(ScopeRequest Scope) : ICommand<Result<Guid>>;
