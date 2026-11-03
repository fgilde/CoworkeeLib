using Coworkee.Application.Authorization;
using Coworkee.Contracts.Social;

namespace Coworkee.Social;

internal sealed class SocialPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(SocialPermissions.GroupName, "Comments, tags, ratings and reactions")
            .Add(SocialPermissions.Comments.View, "View comments")
            .Add(SocialPermissions.Comments.Create, "Write comments", SocialPermissions.Comments.View)
            .Add(SocialPermissions.Comments.Moderate, "Moderate comments", SocialPermissions.Comments.Create)
            .Add(SocialPermissions.Comments.SetStatus, "Open and close comments", SocialPermissions.Comments.View)
            .Add(SocialPermissions.Tags.View, "View tags")
            .Add(SocialPermissions.Tags.Create, "Tag entities", SocialPermissions.Tags.View)
            .Add(SocialPermissions.Tags.Moderate, "Manage tag sets", SocialPermissions.Tags.Create)
            .Add(SocialPermissions.Ratings.View, "View ratings")
            .Add(SocialPermissions.Ratings.Create, "Rate entities", SocialPermissions.Ratings.View)
            .Add(SocialPermissions.Ratings.Moderate, "Moderate ratings", SocialPermissions.Ratings.Create)
            .Add(SocialPermissions.Reactions.View, "View reactions")
            .Add(SocialPermissions.Reactions.Create, "React with emojis", SocialPermissions.Reactions.View);
}
