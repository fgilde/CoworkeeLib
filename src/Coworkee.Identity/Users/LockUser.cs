using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record LockUser(Guid Id, DateTimeOffset? Until) : ICommand<Result>;
