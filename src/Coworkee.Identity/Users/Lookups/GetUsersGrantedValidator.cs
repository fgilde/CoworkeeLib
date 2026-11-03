using Coworkee.Application.Authorization;
using Coworkee.Application.Paging;
using FluentValidation;

namespace Coworkee.Identity.Users.Lookups;

internal sealed class GetUsersGrantedValidator : AbstractValidator<GetUsersGrantedQuery>
{
    public GetUsersGrantedValidator(IPermissionDefinitionManager definitions)
    {
        RuleFor(q => q.Permission).NotEmpty().Must(definitions.Exists).WithMessage("Unknown permission.");
        RuleFor(q => q.Page).SetValidator(new PageRequestValidator());
    }
}
