using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

[RequiresPermission(SocialPermissions.Ratings.Create)]
[Description("Rates an entity with 1 to 5 stars; a second rating replaces the first.")]
public sealed record RateCommand(string EntityType, Guid EntityId, int Stars) : ICommand<Result<RatingDto>>;
