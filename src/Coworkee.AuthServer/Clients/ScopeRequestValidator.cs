using Coworkee.Contracts.Identity;
using FluentValidation;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Clients;

internal sealed class ScopeRequestValidator : AbstractValidator<ScopeRequest>
{
    private static readonly string[] Standard = [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess, Scopes.Phone, Scopes.Address];

    public ScopeRequestValidator()
    {
        RuleFor(s => s.Name).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._:-]+$")
            .Must(n => !Standard.Contains(n)).WithMessage("The OpenID Connect scopes are built in.");
        RuleFor(s => s.DisplayName).MaximumLength(200);
        RuleFor(s => s.Description).MaximumLength(1000);
        RuleForEach(s => s.Resources).NotEmpty().MaximumLength(200);
    }
}
