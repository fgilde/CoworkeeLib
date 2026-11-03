using FluentValidation;

namespace Coworkee.Social;

internal sealed class AddCommentValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentValidator()
    {
        RuleFor(c => c.Text).NotEmpty().MaximumLength(SocialLimits.CommentText);
        RuleFor(c => c.Status).IsInEnum();
        RuleFor(c => c.Properties).Must(CommentPropertyRules.Valid).WithMessage(CommentPropertyRules.Message);
    }
}
