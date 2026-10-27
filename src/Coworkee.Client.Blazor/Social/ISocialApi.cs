using Coworkee.Contracts.Social;

namespace Coworkee.Client.Blazor.Social;

public interface ISocialApi
{
    Task<CommentThreadDto> GetCommentsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<CommentDto> AddCommentAsync(string entityType, Guid entityId, string text, Guid? parentId = null, CancellationToken cancellationToken = default);

    /// <summary>A comment with a status (Open: waits for an answer) or app values.</summary>
    Task<CommentDto> AddCommentAsync(string entityType, Guid entityId, CommentRequest request, CancellationToken cancellationToken = default);

    Task<CommentDto> EditCommentAsync(Guid id, string text, CancellationToken cancellationToken = default);

    /// <summary>Needs Social.Comments.SetStatus; authors change the status of their own comments with an edit.</summary>
    Task<CommentDto> SetCommentStatusAsync(Guid id, CommentStatus status, CancellationToken cancellationToken = default);

    Task DeleteCommentAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TagDto>> GetTagsAsync(string entityType, CancellationToken cancellationToken = default);

    Task<EntityTagsDto> GetEntityTagsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<EntityTagsDto> SaveEntityTagsAsync(string entityType, Guid entityId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetTaggedAsync(string entityType, string tag, CancellationToken cancellationToken = default);

    Task<RatingDto> GetRatingAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<RatingDto> RateAsync(string entityType, Guid entityId, int stars, CancellationToken cancellationToken = default);

    Task<RatingDto> ClearRatingAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<ReactionsDto> GetReactionsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Adds the emoji of the signed-in user, or takes it back when it is there already.</summary>
    Task<ReactionsDto> ToggleReactionAsync(string entityType, Guid entityId, string emoji, CancellationToken cancellationToken = default);
}
