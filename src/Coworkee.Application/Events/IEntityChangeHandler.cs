using Coworkee.Application.Messaging;

namespace Coworkee.Application.Events;

/// <summary>
/// Handles saved changes of <typeparamref name="TEntity"/>, durably: an outbox row per change, after the commit, retried, once per handler, as the user and
/// organisation that saved it. Only types published with <c>AddCoworkeeEvents(e => e.PublishChanges&lt;TEntity&gt;())</c> or <c>[PublishChanges]</c> write
/// those rows. Found like every other handler (<c>AddMessagingFromAssembly</c>).
/// </summary>
public interface IEntityChangeHandler<TEntity> : IDomainEventHandler<EntityChange<TEntity>>
    where TEntity : class;
