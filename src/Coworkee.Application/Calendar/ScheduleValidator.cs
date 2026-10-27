using Coworkee.Contracts.Reporting;
using FluentValidation;

namespace Coworkee.Application.Calendar;

/// <summary>A <see cref="ReportSchedule"/> that can run: a known time zone and the weekday or day its frequency needs.</summary>
public sealed class ScheduleValidator : AbstractValidator<ReportSchedule>
{
    public ScheduleValidator()
    {
        RuleFor(s => s.Frequency).IsInEnum();
        RuleFor(s => s.TimeZone).Must(z => ReportSchedules.Zone(z) is not null).WithMessage("Choose a known time zone, e.g. Europe/Berlin.");
        RuleFor(s => s.Weekday).NotNull().When(s => s.Frequency == SubscriptionFrequency.Weekly || s is { Frequency: SubscriptionFrequency.Monthly, Week: not null })
            .WithMessage("Choose the weekday.");
        RuleFor(s => s.DayOfMonth).NotNull().InclusiveBetween(1, 31).When(s => s is { Frequency: SubscriptionFrequency.Monthly, Week: null }).WithMessage("Choose a day from 1 to 31.");
        RuleFor(s => s.Week).IsInEnum().When(s => s.Week is not null);
        RuleFor(s => s.Every).InclusiveBetween(1, 12).WithMessage("Repeat every 1 to 12 days, weeks or months.");
    }
}
