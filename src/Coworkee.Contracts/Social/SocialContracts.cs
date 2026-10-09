namespace Coworkee.Contracts.Social;

public static class SocialPermissions
{
    public const string GroupName = "Social";

    public static class Comments
    {
        public const string View = "Social.Comments.View";
        public const string Create = "Social.Comments.Create";
        public const string Moderate = "Social.Comments.Moderate";
    }

    public static class Tags
    {
        public const string View = "Social.Tags.View";
        public const string Create = "Social.Tags.Create";
        public const string Moderate = "Social.Tags.Moderate";
    }

    public static class Ratings
    {
        public const string View = "Social.Ratings.View";
        public const string Create = "Social.Ratings.Create";
        public const string Moderate = "Social.Ratings.Moderate";
    }
}

public static class SocialTopics
{
    /// <summary>Realtime topic that signals every new, changed or deleted comment of one entity.</summary>
    public static string Comments(string entityType, Guid entityId) => $"comments:{entityType}:{entityId}";
}

public sealed record CommentDto(Guid Id, Guid? ParentId, Guid AuthorId, string Text, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt);

/// <summary>All comments of an entity, oldest first; replies point to their parent.</summary>
public sealed record CommentThreadDto(IReadOnlyList<CommentDto> Comments, bool CanComment, bool CanModerate);

public sealed record CommentRequest(string Text, Guid? ParentId = null);

public sealed record TagDto(string Name, int Count);

public sealed record EntityTagsDto(IReadOnlyList<string> Tags, bool CanEdit);

public sealed record SaveTagsRequest(IReadOnlyList<string> Tags);

public sealed record RatingDto(double Average, int Count, int? Mine, bool CanRate);

public sealed record RateRequest(int Stars);
