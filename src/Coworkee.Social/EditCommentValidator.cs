using FluentValidation;

namespace Coworkee.Social;

internal sealed class EditCommentValidator : AbstractValidator<EditCommentCommand>
{
    public EditCommentValidator()
    {
        RuleFor(c => c.Text).NotEmpty().MaximumLength(SocialLimits.CommentText);
        RuleFor(c => c.Status).IsInEnum();
        RuleFor(c => c.Properties).Must(CommentPropertyRules.Valid).WithMessage(CommentPropertyRules.Message);
    }
}
