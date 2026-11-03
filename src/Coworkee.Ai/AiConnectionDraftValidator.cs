using Coworkee.Contracts.Ai;
using FluentValidation;

namespace Coworkee.Ai;

internal sealed class AiConnectionDraftValidator : AbstractValidator<AiConnectionDraft>
{
    public AiConnectionDraftValidator()
    {
        RuleFor(d => d.Provider).Must(p => AiProviderCatalog.Find(p) is not null).WithMessage($"Expected one of: {string.Join(", ", AiProviderCatalog.Ids)}.");
        RuleFor(d => d.Model).MaximumLength(200);
        RuleFor(d => d.ApiKey).MaximumLength(2000);
        RuleFor(d => d.Endpoint).MaximumLength(500)
            .Must(e => string.IsNullOrWhiteSpace(e) || (Uri.TryCreate(e.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            .WithMessage("Expected an http or https address.");
        RuleFor(d => d.Headers).MaximumLength(4000)
            .Must(h => AiEndpoints.InvalidHeaderLine(h) is null).WithMessage(d => $"Expected lines \"Name: value\", not \"{AiEndpoints.InvalidHeaderLine(d.Headers)}\".");
        RuleFor(d => d.AuthHeader).Must(h => string.IsNullOrWhiteSpace(h) || AiEndpoints.IsHeaderName(h.Trim())).WithMessage("Expected a header name such as api-key.");
    }
}
