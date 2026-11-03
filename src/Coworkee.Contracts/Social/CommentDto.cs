namespace Coworkee.Contracts.Social;

public sealed record CommentDto(Guid Id, Guid? ParentId, Guid AuthorId, string Text, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt)
{
    /// <summary>With comment reactions turned on and the right to see them.</summary>
    public IReadOnlyList<ReactionDto> Reactions { get; init; } = [];

    public CommentStatus Status { get; init; }

    /// <summary>App values of the comment, e.g. a reference number.</summary>
    public IReadOnlyDictionary<string, string>? Properties { get; init; }
}
