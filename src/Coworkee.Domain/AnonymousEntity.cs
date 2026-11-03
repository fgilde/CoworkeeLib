namespace Coworkee.Domain;

/// <summary>An <see cref="IAnonymous"/> entity with a random id (<see cref="Guid.NewGuid"/>) instead of a time ordered one.</summary>
public abstract class AnonymousEntity : Entity, IAnonymous
{
    protected AnonymousEntity() => Id = Guid.NewGuid();
}
