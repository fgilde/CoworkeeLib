using FluentValidation;

namespace Coworkee.Identity.ApiKeys;

internal sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKey>
{
    public CreateApiKeyValidator()
    {
        RuleFor(c => c.Key.Name).NotEmpty().MaximumLength(ApiKeyLimits.Name);
        RuleFor(c => c.Key.Description).MaximumLength(ApiKeyLimits.Description);
        RuleFor(c => c.Key.Scopes).NotEmpty().Must(s => s.Count <= 500);
    }
}
