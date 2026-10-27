using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Reporting.Subscriptions;

[RequiresPermission(ReportPermissions.View)]
[Description("Lists the signed-in user's report subscriptions (reports mailed on a schedule) with their next and last run.")]
public sealed record GetMyReportSubscriptionsQuery : IQuery<Result<IReadOnlyList<ReportSubscriptionDto>>>;

[RequiresPermission(ReportPermissions.Subscriptions)]
[AiTool(Exclude = true)]
public sealed record GetAllReportSubscriptionsQuery : IQuery<Result<IReadOnlyList<ReportSubscriptionDto>>>;

[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record CreateReportSubscriptionCommand(SaveReportSubscriptionRequest Subscription) : ICommand<Result<ReportSubscriptionDto>>;

[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record UpdateReportSubscriptionCommand(Guid Id, SaveReportSubscriptionRequest Subscription) : ICommand<Result<ReportSubscriptionDto>>;

/// <summary>Ends one's own subscription; holders of <see cref="ReportPermissions.Subscriptions"/> end anyone's.</summary>
[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
public sealed record DeleteReportSubscriptionCommand(Guid Id) : ICommand<Result>;

[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
public sealed record SendReportSubscriptionNowCommand(Guid Id) : ICommand<Result<ReportSubscriptionDto>>;

internal sealed class SaveReportSubscriptionValidator : AbstractValidator<SaveReportSubscriptionRequest>
{
    public SaveReportSubscriptionValidator()
    {
        RuleFor(s => s.Schedule).NotNull();
        RuleFor(s => s.Schedule.TimeZone).Must(z => ReportSchedules.Zone(z) is not null).WithMessage("Choose a known time zone, e.g. Europe/Berlin.");
        RuleFor(s => s.Schedule.Weekday).NotNull().When(s => s.Schedule.Frequency == SubscriptionFrequency.Weekly).WithMessage("Choose the weekday.");
        RuleFor(s => s.Schedule.DayOfMonth).NotNull().InclusiveBetween(1, 31).When(s => s.Schedule.Frequency == SubscriptionFrequency.Monthly).WithMessage("Choose a day from 1 to 31.");
        RuleFor(s => s.Schedule.Frequency).IsInEnum();
        RuleFor(s => s.DateRange!).Must(r => r.Preset != DateRangePreset.Custom || (r.From is not null && r.To is not null && r.From <= r.To))
            .When(s => s.DateRange is not null).WithMessage("A custom date range needs a start and an end.");
        RuleFor(s => s.Filters).Must(f => f is null || f.Count <= 50).WithMessage("At most 50 filters.");
    }
}

internal sealed class CreateReportSubscriptionValidator : AbstractValidator<CreateReportSubscriptionCommand>
{
    public CreateReportSubscriptionValidator() => RuleFor(c => c.Subscription).SetValidator(new SaveReportSubscriptionValidator());
}

internal sealed class UpdateReportSubscriptionValidator : AbstractValidator<UpdateReportSubscriptionCommand>
{
    public UpdateReportSubscriptionValidator() => RuleFor(c => c.Subscription).SetValidator(new SaveReportSubscriptionValidator());
}
