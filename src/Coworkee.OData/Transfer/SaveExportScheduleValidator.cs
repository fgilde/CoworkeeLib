using FluentValidation;

namespace Coworkee.OData.Transfer;

internal sealed class SaveExportScheduleValidator : AbstractValidator<SaveExportScheduleCommand>
{
    public SaveExportScheduleValidator()
    {
        RuleFor(c => c.Schedule).NotNull();
        RuleFor(c => c.Schedule.EntitySet).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Schedule.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Schedule.Query).MaximumLength(4000);
        RuleFor(c => c.Schedule.FileName).MaximumLength(200);
        RuleFor(c => c.Schedule.Schedule).NotNull().SetValidator(new Application.Calendar.ScheduleValidator());
        RuleFor(c => c.Schedule.Recipients).Must(r => DataExports.InvalidRecipients(Clean(r)) is null)
            .WithMessage(c => DataExports.InvalidRecipients(Clean(c.Schedule.Recipients)));
    }

    internal static List<string> Clean(IReadOnlyList<string>? recipients) =>
        recipients?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
}
