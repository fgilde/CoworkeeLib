using FluentValidation;

namespace Coworkee.AuthServer.Clients;

internal sealed class UpdateScopeValidator : AbstractValidator<UpdateScope>
{
    public UpdateScopeValidator() => RuleFor(c => c.Scope).SetValidator(new ScopeRequestValidator());
}
