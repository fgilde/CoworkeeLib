using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Ratings.View)]
[Description("Reads the average rating of an entity, how many people rated it and the own rating.")]
public sealed record GetRatingQuery(string EntityType, Guid EntityId) : IQuery<Result<RatingDto>>;

[RequiresPermission(SocialPermissions.Ratings.Create)]
[Description("Rates an entity with 1 to 5 stars; a second rating replaces the first.")]
public sealed record RateCommand(string EntityType, Guid EntityId, int Stars) : ICommand<Result<RatingDto>>;

[RequiresPermission(SocialPermissions.Ratings.Create)]
[Description("Takes back the own rating of an entity; moderators may remove the rating of another user.")]
public sealed record ClearRatingCommand(string EntityType, Guid EntityId, Guid? UserId = null) : ICommand<Result<RatingDto>>;

internal sealed class RateValidator : AbstractValidator<RateCommand>
{
    public RateValidator() => RuleFor(c => c.Stars).InclusiveBetween(1, 5);
}
