using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Comments.Create)]
[Description("Deletes an own comment with its replies; moderators may delete any comment.")]
public sealed record DeleteCommentCommand(Guid Id) : ICommand<Result>;
