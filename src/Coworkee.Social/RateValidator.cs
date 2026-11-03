using FluentValidation;

namespace Coworkee.Social;

internal sealed class RateValidator : AbstractValidator<RateCommand>
{
    public RateValidator() => RuleFor(c => c.Stars).InclusiveBetween(1, 5);
}
