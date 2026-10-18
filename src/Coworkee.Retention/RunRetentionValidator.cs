using FluentValidation;

namespace Coworkee.Retention;

internal sealed class RunRetentionValidator : AbstractValidator<RunRetentionCommand>
{
    public RunRetentionValidator() => RuleFor(c => c.Days).InclusiveBetween(1, RetentionSettingDefinitions.MaxDays);
}
