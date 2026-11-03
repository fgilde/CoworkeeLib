using FluentValidation;

namespace Coworkee.Reporting.Subscriptions;

internal sealed class CreateReportSubscriptionValidator : AbstractValidator<CreateReportSubscriptionCommand>
{
    public CreateReportSubscriptionValidator() => RuleFor(c => c.Subscription).SetValidator(new SaveReportSubscriptionValidator());
}
