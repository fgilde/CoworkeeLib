using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Comments.Create)]
[Description("Changes the text of an own comment and its app values when given; changing its status needs Social.Comments.SetStatus.")]
public sealed record EditCommentCommand(Guid Id, string Text, CommentStatus? Status = null, IReadOnlyDictionary<string, string>? Properties = null) : ICommand<Result<CommentDto>>;
