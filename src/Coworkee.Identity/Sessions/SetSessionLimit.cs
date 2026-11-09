using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Identity.Sessions;

/// <summary>The sessions a user may have at once: null takes the organisation's setting, 0 is unlimited; sessions he has stay until he signs in again.</summary>
[RequiresPermission(IdentityPermissions.Sessions.Manage)]
public sealed record SetSessionLimit(Guid UserId, int? MaxSessions) : ICommand<Result>;

internal sealed class SetSessionLimitValidator : AbstractValidator<SetSessionLimit>
{
    public SetSessionLimitValidator() => RuleFor(c => c.MaxSessions).InclusiveBetween(0, 1000).When(c => c.MaxSessions is not null);
}
