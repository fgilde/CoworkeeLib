using FluentValidation;

namespace Coworkee.Reporting;

internal sealed class CreateReportValidator : AbstractValidator<CreateReportCommand>
{
    public CreateReportValidator() => RuleFor(c => c.Report).SetValidator(new SaveReportValidator());
}
