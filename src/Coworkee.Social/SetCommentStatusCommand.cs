using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Comments.SetStatus)]
[Description("Sets the status of a comment: None, Open (waits for an answer) or Closed.")]
public sealed record SetCommentStatusCommand(Guid Id, CommentStatus Status) : ICommand<Result<CommentDto>>;
