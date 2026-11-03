using FluentValidation;

namespace Coworkee.Reporting.Subscriptions;

internal sealed class UpdateReportSubscriptionValidator : AbstractValidator<UpdateReportSubscriptionCommand>
{
    public UpdateReportSubscriptionValidator() => RuleFor(c => c.Subscription).SetValidator(new SaveReportSubscriptionValidator());
}
