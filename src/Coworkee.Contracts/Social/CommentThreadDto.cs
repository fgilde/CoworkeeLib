namespace Coworkee.Contracts.Social;

/// <summary>All comments of an entity, oldest first; replies point to their parent.</summary>
public sealed record CommentThreadDto(IReadOnlyList<CommentDto> Comments, bool CanComment, bool CanModerate)
{
    /// <summary>Comment reactions are on and the user may see them.</summary>
    public bool ShowReactions { get; init; }

    public bool CanReact { get; init; }

    /// <summary>May set the status of every comment (Social.Comments.SetStatus).</summary>
    public bool CanSetStatus { get; init; }
}
