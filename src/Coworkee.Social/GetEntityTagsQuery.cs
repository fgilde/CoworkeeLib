using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Tags.View)]
[Description("Reads the tags of one entity.")]
public sealed record GetEntityTagsQuery(string EntityType, Guid EntityId) : IQuery<Result<EntityTagsDto>>;
