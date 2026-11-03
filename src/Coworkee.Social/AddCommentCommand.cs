using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Comments.Create)]
[Description("Comments on an entity, or replies to a comment when ParentId is set; Status Open marks a question or request.")]
public sealed record AddCommentCommand(
    string EntityType, Guid EntityId, string Text, Guid? ParentId = null, CommentStatus Status = CommentStatus.None, IReadOnlyDictionary<string, string>? Properties = null)
    : ICommand<Result<CommentDto>>;
