using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Ratings.View)]
[Description("Reads the average rating of an entity, how many people rated it and the own rating.")]
public sealed record GetRatingQuery(string EntityType, Guid EntityId) : IQuery<Result<RatingDto>>;
