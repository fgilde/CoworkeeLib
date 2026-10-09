using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Social;

namespace Coworkee.Client.Blazor.Social;

internal sealed class SocialApi(HttpClient http) : ApiClientBase(http), ISocialApi
{
    public Task<CommentThreadDto> GetCommentsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
        GetAsync<CommentThreadDto>(Url("comments", entityType, entityId), cancellationToken);

    public Task<CommentDto> AddCommentAsync(string entityType, Guid entityId, string text, Guid? parentId = null, CancellationToken cancellationToken = default) =>
        SendAsync<CommentDto>(HttpMethod.Post, Url("comments", entityType, entityId), new CommentRequest(text, parentId), cancellationToken);

    public Task<CommentDto> EditCommentAsync(Guid id, string text, CancellationToken cancellationToken = default) =>
        SendAsync<CommentDto>(HttpMethod.Put, $"api/v1/comments/{id}", new CommentRequest(text), cancellationToken);

    public Task DeleteCommentAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/comments/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<TagDto>> GetTagsAsync(string entityType, CancellationToken cancellationToken = default) =>
        await GetAsync<TagDto[]>($"api/v1/tags/{Uri.EscapeDataString(entityType)}", cancellationToken);

    public Task<EntityTagsDto> GetEntityTagsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
        GetAsync<EntityTagsDto>(Url("tags", entityType, entityId), cancellationToken);

    public Task<EntityTagsDto> SaveEntityTagsAsync(string entityType, Guid entityId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) =>
        SendAsync<EntityTagsDto>(HttpMethod.Put, Url("tags", entityType, entityId), new SaveTagsRequest(tags), cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetTaggedAsync(string entityType, string tag, CancellationToken cancellationToken = default) =>
        await GetAsync<Guid[]>($"api/v1/tags/{Uri.EscapeDataString(entityType)}/entities?tag={Uri.EscapeDataString(tag)}", cancellationToken);

    public Task<RatingDto> GetRatingAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
        GetAsync<RatingDto>(Url("ratings", entityType, entityId), cancellationToken);

    public Task<RatingDto> RateAsync(string entityType, Guid entityId, int stars, CancellationToken cancellationToken = default) =>
        SendAsync<RatingDto>(HttpMethod.Put, Url("ratings", entityType, entityId), new RateRequest(stars), cancellationToken);

    public Task<RatingDto> ClearRatingAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
        SendAsync<RatingDto>(HttpMethod.Delete, Url("ratings", entityType, entityId), null, cancellationToken);

    private static string Url(string area, string entityType, Guid entityId) => $"api/v1/{area}/{Uri.EscapeDataString(entityType)}/{entityId}";
}
