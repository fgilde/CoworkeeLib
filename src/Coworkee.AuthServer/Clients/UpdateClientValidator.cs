using FluentValidation;

namespace Coworkee.AuthServer.Clients;

internal sealed class UpdateClientValidator : AbstractValidator<UpdateClient>
{
    public UpdateClientValidator() => RuleFor(c => c.Client).SetValidator(new ClientRequestValidator());
}
