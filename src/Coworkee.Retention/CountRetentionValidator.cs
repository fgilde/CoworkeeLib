using FluentValidation;

namespace Coworkee.Retention;

internal sealed class CountRetentionValidator : AbstractValidator<CountRetentionQuery>
{
    public CountRetentionValidator() => RuleFor(q => q.Days).InclusiveBetween(1, RetentionSettingDefinitions.MaxDays);
}
