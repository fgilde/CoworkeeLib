using FluentValidation;

namespace Coworkee.Localization.Features.Texts;

internal sealed class ReportMissingTextsValidator : AbstractValidator<ReportMissingTextsCommand>
{
    public ReportMissingTextsValidator()
    {
        RuleFor(c => c.Keys).NotNull().Must(k => k.Count <= 200).WithMessage("At most 200 keys at once.");
        RuleForEach(c => c.Keys).NotEmpty().MaximumLength(500);
    }
}
