using FluentValidation;

namespace Coworkee.Reporting;

internal sealed class GenerateReportDefinitionValidator : AbstractValidator<GenerateReportDefinitionQuery>
{
    public GenerateReportDefinitionValidator() => RuleFor(q => q.Prompt).NotEmpty().MaximumLength(4000);
}
