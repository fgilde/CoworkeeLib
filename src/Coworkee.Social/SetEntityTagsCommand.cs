using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Tags.Create)]
[Description("Replaces the tags of one entity; unknown tags join the tag set of the entity type.")]
public sealed record SetEntityTagsCommand(string EntityType, Guid EntityId, IReadOnlyList<string> Tags) : ICommand<Result<EntityTagsDto>>;
