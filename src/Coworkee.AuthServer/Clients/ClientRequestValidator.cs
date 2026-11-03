using Coworkee.Contracts.Identity;
using FluentValidation;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Clients;

internal sealed class ClientRequestValidator : AbstractValidator<ClientRequest>
{
    public ClientRequestValidator()
    {
        RuleFor(c => c.ClientId).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._-]+$");
        RuleFor(c => c.DisplayName).MaximumLength(200);
        RuleFor(c => c.ClientType).Must(t => t is ClientTypes.Public or ClientTypes.Confidential).WithMessage("Choose public or confidential.");
        RuleFor(c => c.ConsentType).Must(t => t is ConsentTypes.Implicit or ConsentTypes.Explicit).WithMessage("Choose implicit or explicit.");
        RuleFor(c => c.GrantTypes).NotEmpty().Must(g => g.All(ClientGrantTypes.All.Contains)).WithMessage("Unknown grant type.");
        RuleFor(c => c.RedirectUris).NotEmpty().When(c => c.GrantTypes.Contains(ClientGrantTypes.AuthorizationCode));
        RuleFor(c => c.ClientType).Equal(ClientTypes.Confidential).When(c => c.GrantTypes.Contains(ClientGrantTypes.ClientCredentials))
            .WithMessage("Service clients have to be confidential.");
        RuleForEach(c => c.Permissions).NotEmpty();
        RuleForEach(c => c.RedirectUris).Must(BeAbsolute).WithMessage("'{PropertyValue}' is not an absolute address.");
        RuleForEach(c => c.PostLogoutRedirectUris).Must(BeAbsolute).WithMessage("'{PropertyValue}' is not an absolute address.");
        RuleForEach(c => c.Scopes).NotEmpty().Matches("^[A-Za-z0-9._:-]+$");
        RuleFor(c => c.ClientUri).Must(ClientApp.IsWebAddress).When(c => !string.IsNullOrWhiteSpace(c.ClientUri)).WithMessage("'{PropertyValue}' is not a web address.");
        RuleFor(c => c.LogoUrl).Must(ClientApp.IsWebAddress).When(c => !string.IsNullOrWhiteSpace(c.LogoUrl)).WithMessage("'{PropertyValue}' is not a web address.");
        RuleFor(c => c.Description).MaximumLength(500);
    }

    // outside Windows "/out" parses as an absolute file path (file:///out)
    private static bool BeAbsolute(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var address) && !address.IsFile;
}
