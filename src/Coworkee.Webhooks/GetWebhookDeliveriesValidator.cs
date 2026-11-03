using FluentValidation;

namespace Coworkee.Webhooks;

internal sealed class GetWebhookDeliveriesValidator : AbstractValidator<GetWebhookDeliveriesQuery>
{
    public GetWebhookDeliveriesValidator() => RuleFor(q => q.Take).InclusiveBetween(1, 200);
}
