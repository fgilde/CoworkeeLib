using Coworkee.Application.Paging;
using FluentValidation;

namespace Coworkee.Identity.Groups;

internal sealed class GetGroupsValidator : AbstractValidator<GetGroups>
{
    public GetGroupsValidator() => RuleFor(q => q.Page).SetValidator(new PageRequestValidator());
}
