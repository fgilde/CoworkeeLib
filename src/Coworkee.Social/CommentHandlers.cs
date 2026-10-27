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
    CoworkeeDbContext db, SocialGuard guard, SocialOptions options, IPermissionChecker permissions, ICurrentUser currentUser, TimeProvider clock, IServiceProvider services)
    : IHandler<GetCommentsQuery, Result<CommentThreadDto>>,
      IHandler<AddCommentCommand, Result<CommentDto>>,
      IHandler<EditCommentCommand, Result<CommentDto>>,
      IHandler<SetCommentStatusCommand, Result<CommentDto>>,
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
        var showReactions = guard.HasCommentReactions && await permissions.IsGrantedAsync(SocialPermissions.Reactions.View, cancellationToken);
        var reactions = showReactions ? await ReactionSummary.OfAsync(db, SocialReactions.Comment, [.. comments.Select(c => c.Id)], cancellationToken) : [];
        return new CommentThreadDto([.. comments.Select(c => ToDto(c) with { Reactions = reactions.GetValueOrDefault(c.Id, []) })], canComment, canComment && await IsModeratorAsync(cancellationToken))
        {
            ShowReactions = showReactions,
            CanReact = showReactions && await permissions.IsGrantedAsync(SocialPermissions.Reactions.Create, cancellationToken)
                && await guard.AllowsAsync(Access(query.EntityType, query.EntityId, write: true), cancellationToken),
            CanSetStatus = await permissions.IsGrantedAsync(SocialPermissions.Comments.SetStatus, cancellationToken)
                && await guard.AllowsAsync(Access(query.EntityType, query.EntityId, write: true), cancellationToken),
        };
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

        var text = command.Text.Trim();
        if (await ValidateAsync(new(CommentAction.Added, command.EntityType, command.EntityId, text, command.Status, command.Properties, null, command.ParentId), cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var comment = new Comment
        {
            TenantId = currentUser.TenantId!.Value,
            EntityType = command.EntityType,
            EntityId = command.EntityId,
            ParentId = command.ParentId,
            AuthorId = Me,
            Text = text,
            Status = command.Status,
            Properties = command.Properties is { Count: > 0 } properties ? new(properties) : null,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Add(comment);
        comment.Changed(CommentAction.Added);
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

        var text = command.Text.Trim();
        var status = command.Status ?? comment.Status;
        var properties = command.Properties ?? comment.Properties;
        if (await ValidateAsync(new(CommentAction.Edited, comment.EntityType, comment.EntityId, text, status, properties, comment.Id, comment.ParentId), cancellationToken) is { } invalid)
        {
            return invalid;
        }

        (comment.Text, comment.Status, comment.EditedAt) = (text, status, clock.GetUtcNow());
        comment.Properties = command.Properties is null ? comment.Properties : command.Properties.Count > 0 ? new(command.Properties) : null;
        comment.Changed(CommentAction.Edited);
        return ToDto(comment);
    }

    public async Task<Result<CommentDto>> HandleAsync(SetCommentStatusCommand command, CancellationToken cancellationToken)
    {
        var comment = await db.Set<Comment>().SingleOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (comment is null)
        {
            return UnknownComment;
        }

        if (await guard.CheckAsync(Access(comment.EntityType, comment.EntityId, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        if (await ValidateAsync(new(CommentAction.StatusChanged, comment.EntityType, comment.EntityId, comment.Text, command.Status, comment.Properties, comment.Id, comment.ParentId),
                cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (comment.Status != command.Status)
        {
            comment.Status = command.Status;
            comment.Changed(CommentAction.StatusChanged);
        }

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

        if (await ValidateAsync(new(CommentAction.Deleted, comment.EntityType, comment.EntityId, comment.Text, comment.Status, comment.Properties, comment.Id, comment.ParentId),
                cancellationToken) is { } invalid)
        {
            return invalid;
        }

        // the replies go along; loaded, so EF removes them where the database does not cascade (SQL Server)
        var thread = await Of(comment.EntityType, comment.EntityId).ToListAsync(cancellationToken);
        var gone = Subtree(comment.Id, thread.ToLookup(c => c.ParentId)).ToList();
        foreach (var removed in thread.Where(c => gone.Contains(c.Id)))
        {
            removed.Changed(CommentAction.Deleted);
        }

        db.RemoveRange(await db.Set<Reaction>().Where(r => r.EntityType == SocialReactions.Comment && gone.Contains(r.EntityId)).ToListAsync(cancellationToken));
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

    private async Task<Error?> ValidateAsync(CommentValidation validation, CancellationToken cancellationToken)
    {
        foreach (var check in options.CommentChecks)
        {
            if (await check(services, validation, cancellationToken) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    private static IEnumerable<Guid> Subtree(Guid id, ILookup<Guid?, Comment> replies) => replies[id].SelectMany(r => Subtree(r.Id, replies)).Prepend(id);

    private Task<bool> IsModeratorAsync(CancellationToken cancellationToken) => permissions.IsGrantedAsync(SocialPermissions.Comments.Moderate, cancellationToken);

    private IQueryable<Comment> Of(string entityType, Guid entityId) => db.Set<Comment>().Where(c => c.EntityType == entityType && c.EntityId == entityId);

    private static SocialAccess Access(string entityType, Guid entityId, bool write) => new(SocialFeature.Comments, entityType, entityId, write);

    private static CommentDto ToDto(Comment c) => new(c.Id, c.ParentId, c.AuthorId, c.Text, c.CreatedAt, c.EditedAt) { Status = c.Status, Properties = c.Properties };
}
