using Coworkee.Contracts.Settings;
using FluentValidation;

namespace Coworkee.Settings;

internal sealed class SetHeadScriptValidator : AbstractValidator<SetHeadScript>
{
    public SetHeadScriptValidator() => RuleFor(c => c.Value).MaximumLength(HeadScriptSettings.MaxLength);
}
