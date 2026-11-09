using FluentValidation;

namespace Coworkee.Identity.Roles;

internal sealed class TransferRoleUsersValidator : AbstractValidator<TransferRoleUsers>
{
    public TransferRoleUsersValidator()
    {
        RuleFor(c => c.Request.TargetRoleIds).NotEmpty()
            .Must((c, ids) => !ids.Contains(c.RoleId)).WithMessage("Choose roles other than the role the users come from.");
        // an empty choice must not read as "all users"
        RuleFor(c => c.Request.UserIds).Must(ids => ids is not { Count: 0 }).WithMessage("Choose at least one user.");
    }
}
