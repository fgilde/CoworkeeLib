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
[Description("Comments on an entity, or replies to a comment when ParentId is set.")]
public sealed record AddCommentCommand(string EntityType, Guid EntityId, string Text, Guid? ParentId = null) : ICommand<Result<CommentDto>>;

[RequiresPermission(SocialPermissions.Comments.Create)]
[Description("Changes the text of an own comment.")]
public sealed record EditCommentCommand(Guid Id, string Text) : ICommand<Result<CommentDto>>;

[RequiresPermission(SocialPermissions.Comments.Create)]
[Description("Deletes an own comment with its replies; moderators may delete any comment.")]
public sealed record DeleteCommentCommand(Guid Id) : ICommand<Result>;

internal sealed class AddCommentValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentValidator() => RuleFor(c => c.Text).NotEmpty().MaximumLength(SocialLimits.CommentText);
}

internal sealed class EditCommentValidator : AbstractValidator<EditCommentCommand>
{
    public EditCommentValidator() => RuleFor(c => c.Text).NotEmpty().MaximumLength(SocialLimits.CommentText);
}
