using FluentValidation;

namespace Coworkee.Reporting;

internal sealed class UpdateReportValidator : AbstractValidator<UpdateReportCommand>
{
    public UpdateReportValidator() => RuleFor(c => c.Report).SetValidator(new SaveReportValidator());
}
