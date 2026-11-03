using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Ratings.Create)]
[Description("Takes back the own rating of an entity; moderators may remove the rating of another user.")]
public sealed record ClearRatingCommand(string EntityType, Guid EntityId, Guid? UserId = null) : ICommand<Result<RatingDto>>;
