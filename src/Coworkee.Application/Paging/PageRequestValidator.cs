using Coworkee.Contracts;
using FluentValidation;

namespace Coworkee.Application.Paging;

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(p => p.Page).GreaterThanOrEqualTo(1);
        RuleFor(p => p.PageSize).InclusiveBetween(1, 200);
    }
}
