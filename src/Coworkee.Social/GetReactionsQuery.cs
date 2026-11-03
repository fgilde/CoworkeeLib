using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Reactions.View)]
[Description("Reads the emoji reactions of an entity with who reacted.")]
public sealed record GetReactionsQuery(string EntityType, Guid EntityId) : IQuery<Result<ReactionsDto>>;
