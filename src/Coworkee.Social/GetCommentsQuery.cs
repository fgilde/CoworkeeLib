using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Comments.View)]
[Description("Reads the comments of an entity, oldest first; replies carry the id of their parent.")]
public sealed record GetCommentsQuery(string EntityType, Guid EntityId) : IQuery<Result<CommentThreadDto>>;
