namespace Coworkee.Contracts.Social;

public static class SocialPermissions
{
    public const string GroupName = "Social";

    public static class Comments
    {
        public const string View = "Social.Comments.View";
        public const string Create = "Social.Comments.Create";
        public const string Moderate = "Social.Comments.Moderate";

        /// <summary>Sets the status of any comment (open, closed); authors set it on their own comments with Create.</summary>
        public const string SetStatus = "Social.Comments.SetStatus";
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

/// <summary>Whether a comment waits for an answer: an Open comment is a question or request until someone closes it.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<CommentStatus>))]
public enum CommentStatus
{
    None = 0,
    Open = 1,
    Closed = 2,
}

public sealed record CommentDto(Guid Id, Guid? ParentId, Guid AuthorId, string Text, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt)
{
    /// <summary>With comment reactions turned on and the right to see them.</summary>
    public IReadOnlyList<ReactionDto> Reactions { get; init; } = [];

    public CommentStatus Status { get; init; }

    /// <summary>App values of the comment, e.g. a reference number.</summary>
    public IReadOnlyDictionary<string, string>? Properties { get; init; }
}

/// <summary>All comments of an entity, oldest first; replies point to their parent.</summary>
public sealed record CommentThreadDto(IReadOnlyList<CommentDto> Comments, bool CanComment, bool CanModerate)
{
    /// <summary>Comment reactions are on and the user may see them.</summary>
    public bool ShowReactions { get; init; }

    public bool CanReact { get; init; }

    /// <summary>May set the status of every comment (Social.Comments.SetStatus).</summary>
    public bool CanSetStatus { get; init; }
}

/// <param name="Status">On edit: null keeps the status.</param>
/// <param name="Properties">App values (at most 20, keys up to 50 and values up to 500 characters); on edit null keeps them.</param>
public sealed record CommentRequest(string Text, Guid? ParentId = null, CommentStatus? Status = null, IReadOnlyDictionary<string, string>? Properties = null);

public sealed record CommentStatusRequest(CommentStatus Status);

public sealed record TagDto(string Name, int Count);

public sealed record EntityTagsDto(IReadOnlyList<string> Tags, bool CanEdit);

public sealed record SaveTagsRequest(IReadOnlyList<string> Tags);

public sealed record RatingDto(double Average, int Count, int? Mine, bool CanRate);

public sealed record RateRequest(int Stars);
