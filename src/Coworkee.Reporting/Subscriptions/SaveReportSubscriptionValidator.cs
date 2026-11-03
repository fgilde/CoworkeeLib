using Coworkee.Contracts.Reporting;
using FluentValidation;

namespace Coworkee.Reporting.Subscriptions;

internal sealed class SaveReportSubscriptionValidator : AbstractValidator<SaveReportSubscriptionRequest>
{
    public SaveReportSubscriptionValidator()
    {
        RuleFor(s => s.Schedule).NotNull().SetValidator(new Application.Calendar.ScheduleValidator());
        RuleFor(s => s.DateRange!).Must(r => r.Preset != DateRangePreset.Custom || (r.From is not null && r.To is not null && r.From <= r.To))
            .When(s => s.DateRange is not null).WithMessage("A custom date range needs a start and an end.");
        RuleFor(s => s.Filters).Must(f => f is null || f.Count <= 50).WithMessage("At most 50 filters.");
    }
}
