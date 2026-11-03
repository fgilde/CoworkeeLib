using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Reactions.Create)]
[Description("Adds an emoji reaction to an entity, or takes it back when the signed-in user already reacted with it.")]
public sealed record ToggleReactionCommand(string EntityType, Guid EntityId, string Emoji) : ICommand<Result<ReactionsDto>>;
