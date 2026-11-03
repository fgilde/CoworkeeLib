using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Tags.View)]
[Description("Lists the ids of the entities of a type that carry a tag.")]
public sealed record GetTaggedEntitiesQuery(string EntityType, string Tag) : IQuery<Result<IReadOnlyList<Guid>>>;
