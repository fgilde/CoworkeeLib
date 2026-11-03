using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

/// <summary>A ticket for the auth server's <c>/Account/Impersonate</c>, which signs the caller in as <paramref name="UserId"/>.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(IdentityPermissions.Users.Impersonate)]
public sealed record StartImpersonation(Guid UserId) : ICommand<Result<ImpersonationTicketDto>>;
