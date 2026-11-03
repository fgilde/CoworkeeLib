using FluentValidation;

namespace Coworkee.Identity.Groups;

internal sealed class SearchGroupTreeValidator : AbstractValidator<SearchGroupTree>
{
    public SearchGroupTreeValidator() => RuleFor(q => q.Search).MaximumLength(200);
}
