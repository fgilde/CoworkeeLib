using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Tags.Moderate)]
[Description("Removes a tag from the tag set of an entity type and from every entity.")]
public sealed record DeleteTagCommand(string EntityType, string Tag) : ICommand<Result>;
