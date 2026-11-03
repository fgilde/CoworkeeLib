using Coworkee.Application.Paging;
using FluentValidation;

namespace Coworkee.Identity.Users;

internal sealed class GetUsersValidator : AbstractValidator<GetUsers>
{
    public GetUsersValidator() => RuleFor(q => q.Page).SetValidator(new PageRequestValidator());
}
