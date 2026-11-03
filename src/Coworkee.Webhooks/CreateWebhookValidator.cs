using FluentValidation;

namespace Coworkee.Webhooks;

internal sealed class CreateWebhookValidator : AbstractValidator<CreateWebhookCommand>
{
    public CreateWebhookValidator(WebhookUrlGuard guard) => RuleFor(c => c.Request).SetValidator(new WebhookRequestValidator(guard));
}
