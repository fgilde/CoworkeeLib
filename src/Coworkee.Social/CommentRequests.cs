using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Comments.View)]
[Description("Reads the comments of an entity, oldest first; replies carry the id of their parent.")]
public sealed record GetCommentsQuery(string EntityType, Guid EntityId) : IQuery<Result<CommentThreadDto>>;

[RequiresPermission(SocialPermissions.Comments.Create)]
[Description("Comments on an entity, or replies to a comment when ParentId is set; Status Open marks a question or request.")]
public sealed record AddCommentCommand(
    string EntityType, Guid EntityId, string Text, Guid? ParentId = null, CommentStatus Status = CommentStatus.None, IReadOnlyDictionary<string, string>? Properties = null)
    : ICommand<Result<CommentDto>>;

[RequiresPermission(SocialPermissions.Comments.Create)]
[Description("Changes the text of an own comment, and its status or app values when given.")]
public sealed record EditCommentCommand(Guid Id, string Text, CommentStatus? Status = null, IReadOnlyDictionary<string, string>? Properties = null) : ICommand<Result<CommentDto>>;

[RequiresPermission(SocialPermissions.Comments.SetStatus)]
[Description("Sets the status of a comment: None, Open (waits for an answer) or Closed.")]
public sealed record SetCommentStatusCommand(Guid Id, CommentStatus Status) : ICommand<Result<CommentDto>>;

[RequiresPermission(SocialPermissions.Comments.Create)]
[Description("Deletes an own comment with its replies; moderators may delete any comment.")]
public sealed record DeleteCommentCommand(Guid Id) : ICommand<Result>;

internal sealed class AddCommentValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentValidator()
    {
        RuleFor(c => c.Text).NotEmpty().MaximumLength(SocialLimits.CommentText);
        RuleFor(c => c.Status).IsInEnum();
        RuleFor(c => c.Properties).Must(CommentPropertyRules.Valid).WithMessage(CommentPropertyRules.Message);
    }
}

internal sealed class EditCommentValidator : AbstractValidator<EditCommentCommand>
{
    public EditCommentValidator()
    {
        RuleFor(c => c.Text).NotEmpty().MaximumLength(SocialLimits.CommentText);
        RuleFor(c => c.Status).IsInEnum();
        RuleFor(c => c.Properties).Must(CommentPropertyRules.Valid).WithMessage(CommentPropertyRules.Message);
    }
}

internal sealed class SetCommentStatusValidator : AbstractValidator<SetCommentStatusCommand>
{
    public SetCommentStatusValidator() => RuleFor(c => c.Status).IsInEnum();
}

internal static class CommentPropertyRules
{
    public const string Message = "At most 20 values, names up to 50 and values up to 500 characters.";

    public static bool Valid(IReadOnlyDictionary<string, string>? properties) =>
        properties is null || (properties.Count <= SocialLimits.CommentProperties
            && properties.All(p => p.Key.Length is > 0 and <= SocialLimits.PropertyName && (p.Value?.Length ?? 0) <= SocialLimits.PropertyValue));
}
