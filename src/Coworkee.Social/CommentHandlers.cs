using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Social;

internal sealed class CommentHandlers(
    CoworkeeDbContext db, SocialGuard guard, IPermissionChecker permissions, ICurrentUser currentUser, TimeProvider clock, IServiceProvider services)
    : IHandler<GetCommentsQuery, Result<CommentThreadDto>>,
      IHandler<AddCommentCommand, Result<CommentDto>>,
      IHandler<EditCommentCommand, Result<CommentDto>>,
      IHandler<DeleteCommentCommand, Result>
{
    private static readonly Error UnknownComment = Error.NotFound("comments.not_found", "The comment does not exist.");

    private Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Comments need a signed-in user.");

    public async Task<Result<CommentThreadDto>> HandleAsync(GetCommentsQuery query, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(Access(query.EntityType, query.EntityId, write: false), cancellationToken) is { } error)
        {
            return error;
        }

        var comments = await Of(query.EntityType, query.EntityId).AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var canComment = await permissions.IsGrantedAsync(SocialPermissions.Comments.Create, cancellationToken)
            && await guard.AllowsAsync(Access(query.EntityType, query.EntityId, write: true), cancellationToken);
        return new CommentThreadDto(comments.Select(ToDto).ToList(), canComment, canComment && await IsModeratorAsync(cancellationToken));
    }

    public async Task<Result<CommentDto>> HandleAsync(AddCommentCommand command, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(Access(command.EntityType, command.EntityId, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        if (command.ParentId is { } parentId && !await Of(command.EntityType, command.EntityId).AnyAsync(c => c.Id == parentId, cancellationToken))
        {
            return UnknownComment;
        }

        var comment = new Comment
        {
            TenantId = currentUser.TenantId!.Value,
            EntityType = command.EntityType,
            EntityId = command.EntityId,
            ParentId = command.ParentId,
            AuthorId = Me,
            Text = command.Text.Trim(),
            CreatedAt = clock.GetUtcNow(),
        };
        db.Add(comment);
        await NotifyAsync(comment, cancellationToken);
        return ToDto(comment);
    }

    public async Task<Result<CommentDto>> HandleAsync(EditCommentCommand command, CancellationToken cancellationToken)
    {
        var comment = await db.Set<Comment>().SingleOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (comment is null)
        {
            return UnknownComment;
        }

        if (comment.AuthorId != Me)
        {
            return SocialGuard.Forbidden;
        }

        if (await guard.CheckAsync(Access(comment.EntityType, comment.EntityId, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        comment.Text = command.Text.Trim();
        comment.EditedAt = clock.GetUtcNow();
        return ToDto(comment);
    }

    public async Task<Result> HandleAsync(DeleteCommentCommand command, CancellationToken cancellationToken)
    {
        var comment = await db.Set<Comment>().SingleOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (comment is null)
        {
            return UnknownComment;
        }

        if (comment.AuthorId != Me && !await IsModeratorAsync(cancellationToken))
        {
            return SocialGuard.Forbidden;
        }

        if (await guard.CheckAsync(Access(comment.EntityType, comment.EntityId, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        db.Remove(comment);
        return Result.Success();
    }

    private async Task NotifyAsync(Comment comment, CancellationToken cancellationToken)
    {
        if (services.GetService<INotifier>() is not { } notifier)
        {
            return;
        }

        var target = guard.Target(SocialFeature.Comments, comment.EntityType);
        var owner = target.OwnerAsync is { } ownerOf ? await ownerOf(db, comment.EntityId, cancellationToken) : null;
        var participants = await Of(comment.EntityType, comment.EntityId).Select(c => c.AuthorId).Distinct().ToListAsync(cancellationToken);
        var recipients = participants.Append(owner ?? Guid.Empty).Where(id => id != Guid.Empty && id != comment.AuthorId).ToList();
        if (recipients.Count > 0)
        {
            var body = comment.Text.Length > 200 ? comment.Text[..200] + "…" : comment.Text;
            await notifier.NotifyLocalizedAsync(recipients, "social.comment", "New comment", "{0}", [body], target.Link?.Invoke(comment.EntityId), cancellationToken);
        }
    }

    private Task<bool> IsModeratorAsync(CancellationToken cancellationToken) => permissions.IsGrantedAsync(SocialPermissions.Comments.Moderate, cancellationToken);

    private IQueryable<Comment> Of(string entityType, Guid entityId) => db.Set<Comment>().Where(c => c.EntityType == entityType && c.EntityId == entityId);

    private static SocialAccess Access(string entityType, Guid entityId, bool write) => new(SocialFeature.Comments, entityType, entityId, write);

    private static CommentDto ToDto(Comment c) => new(c.Id, c.ParentId, c.AuthorId, c.Text, c.CreatedAt, c.EditedAt);
}
