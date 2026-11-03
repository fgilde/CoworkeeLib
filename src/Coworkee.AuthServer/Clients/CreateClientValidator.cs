using FluentValidation;

namespace Coworkee.AuthServer.Clients;

internal sealed class CreateClientValidator : AbstractValidator<CreateClient>
{
    public CreateClientValidator() => RuleFor(c => c.Client).SetValidator(new ClientRequestValidator());
}
