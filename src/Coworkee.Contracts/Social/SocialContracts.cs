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

    public static class Reactions
    {
        public const string View = "Social.Reactions.View";
        public const string Create = "Social.Reactions.Create";
    }
}

public static class SocialTopics
{
    /// <summary>Realtime topic that signals every new, changed or deleted comment of one entity.</summary>
    public static string Comments(string entityType, Guid entityId) => $"comments:{entityType}:{entityId}";

    /// <summary>Realtime topic that signals every added or taken back reaction of one entity.</summary>
    public static string Reactions(string entityType, Guid entityId) => $"reactions:{entityType}:{entityId}";
}

public static class SocialReactions
{
    /// <summary>The entity type of reactions on comments (with <c>social.CommentReactions()</c>); the id is the comment's.</summary>
    public const string Comment = "Social.Comment";

    /// <summary>The emojis the reaction picker offers by default.</summary>
    public static readonly IReadOnlyList<string> Defaults = ["👍", "❤️", "😂", "🎉", "😮", "😢"];
}

/// <summary>One emoji on an entity with everyone who reacted with it, first reaction first.</summary>
public sealed record ReactionDto(string Emoji, IReadOnlyList<Guid> UserIds);

public sealed record ReactionsDto(IReadOnlyList<ReactionDto> Reactions, bool CanReact);

public sealed record ReactionRequest(string Emoji);

public sealed record CommentDto(Guid Id, Guid? ParentId, Guid AuthorId, string Text, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt)
{
    /// <summary>With comment reactions turned on and the right to see them.</summary>
    public IReadOnlyList<ReactionDto> Reactions { get; init; } = [];
}

/// <summary>All comments of an entity, oldest first; replies point to their parent.</summary>
public sealed record CommentThreadDto(IReadOnlyList<CommentDto> Comments, bool CanComment, bool CanModerate)
{
    /// <summary>Comment reactions are on and the user may see them.</summary>
    public bool ShowReactions { get; init; }

    public bool CanReact { get; init; }
}

public sealed record CommentRequest(string Text, Guid? ParentId = null);

public sealed record TagDto(string Name, int Count);

public sealed record EntityTagsDto(IReadOnlyList<string> Tags, bool CanEdit);

public sealed record SaveTagsRequest(IReadOnlyList<string> Tags);

public sealed record RatingDto(double Average, int Count, int? Mine, bool CanRate);

public sealed record RateRequest(int Stars);
