using Coworkee.Contracts.Social;
using Coworkee.Domain;

namespace Coworkee.Social;

/// <summary>
/// A comment was added, edited, got another status or was deleted. Handlers (<c>IDomainEventHandler&lt;CommentChanged&gt;</c>) run after the commit as the
/// user who did it, so they read the thread as it is now, e.g. to move a quotation to Request while one of its comments is Open.
/// </summary>
public sealed record CommentChanged(Guid CommentId, string EntityType, Guid EntityId, CommentStatus Status, CommentAction Action) : IDomainEvent;
