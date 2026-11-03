using FluentValidation;

namespace Coworkee.AuthServer.Clients;

internal sealed class CreateScopeValidator : AbstractValidator<CreateScope>
{
    public CreateScopeValidator() => RuleFor(c => c.Scope).SetValidator(new ScopeRequestValidator());
}
