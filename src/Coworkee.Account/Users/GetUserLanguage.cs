using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Account.Users;

[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUserLanguage(Guid Id) : IQuery<Result<UserLanguageDto>>;
