using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;

namespace Coworkee.Social;

/// <summary>Open to every signed-in user: it names the app's configuration, no data; the blocks themselves check permissions and rows.</summary>
[Description("Lists which social blocks (comments, tags, ratings, reactions) an entity type takes.")]
public sealed record GetSocialFeaturesQuery(string EntityType) : IQuery<Result<SocialFeaturesDto>>;

internal sealed class GetSocialFeaturesHandler(SocialOptions options) : IHandler<GetSocialFeaturesQuery, Result<SocialFeaturesDto>>
{
    public Task<Result<SocialFeaturesDto>> HandleAsync(GetSocialFeaturesQuery query, CancellationToken cancellationToken)
    {
        bool On(SocialFeature feature) => options.Targets.ContainsKey((feature, query.EntityType));
        return Task.FromResult(Result<SocialFeaturesDto>.Success(new SocialFeaturesDto(On(SocialFeature.Comments), On(SocialFeature.Tags), On(SocialFeature.Ratings), On(SocialFeature.Reactions))));
    }
}
