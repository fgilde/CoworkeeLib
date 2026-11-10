using FluentValidation;

namespace Coworkee.Identity.Permissions;

internal sealed class GetUserPermissionsValidator : AbstractValidator<GetUserPermissions>
{
    public GetUserPermissionsValidator() => RuleFor(q => q.UserId).NotEmpty();
}
