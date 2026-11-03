using FluentValidation;

namespace Coworkee.Webhooks;

internal sealed class UpdateWebhookValidator : AbstractValidator<UpdateWebhookCommand>
{
    public UpdateWebhookValidator(WebhookUrlGuard guard) => RuleFor(c => c.Request).SetValidator(new WebhookRequestValidator(guard));
}
