using Coworkee.Contracts.Webhooks;
using FluentValidation;

namespace Coworkee.Webhooks;

internal sealed class WebhookRequestValidator : AbstractValidator<WebhookRequest>
{
    public WebhookRequestValidator(WebhookUrlGuard guard)
    {
        RuleFor(r => r.Url).NotEmpty().MaximumLength(WebhookLimits.Url).Custom((url, context) =>
        {
            if (!string.IsNullOrEmpty(url) && guard.Check(url) is { } problem)
            {
                context.AddFailure(problem);
            }
        });
        RuleFor(r => r.Events).NotEmpty();
        RuleForEach(r => r.Events).NotEmpty().MaximumLength(WebhookLimits.EventType);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.Secret).Must(s => s is null || WebhookSignature.IsValidSecret(s)).WithMessage("Use a whsec_ secret or at least 16 characters.");
    }
}
