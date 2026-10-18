using FluentValidation;

namespace Coworkee.Retention;

internal sealed class SetRetentionDaysValidator : AbstractValidator<SetRetentionDaysCommand>
{
    public SetRetentionDaysValidator() =>
        RuleForEach(c => c.Days).Must(d => d.Value is null or (>= 0 and <= RetentionSettingDefinitions.MaxDays))
            .WithMessage($"Days are empty or between 0 and {RetentionSettingDefinitions.MaxDays}.");
}
