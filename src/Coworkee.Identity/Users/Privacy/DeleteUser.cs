using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Privacy;

[AiTool(Exclude = true)]
[Application.Authorization.RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record DeleteUser(Guid Id) : ICommand<Result>;
