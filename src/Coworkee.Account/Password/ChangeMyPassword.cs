using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Account.Password;

/// <summary>
/// The own new password, with the rules of the account page: password policy and history, wrong current passwords count towards the lockout,
/// a new security stamp ends the other sessions while the calling one keeps its tokens.
/// </summary>
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record ChangeMyPassword(string CurrentPassword, string NewPassword) : ICommand<Result>;

internal sealed class ChangeMyPasswordValidator : AbstractValidator<ChangeMyPassword>
{
    public ChangeMyPasswordValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(c => c.NewPassword).NotEmpty().MaximumLength(256);
    }
}
