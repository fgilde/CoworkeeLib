using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Changes;

public static class EntityEventServiceCollectionExtensions
{
    /// <summary>
    /// Publishes saved changes of entity types durably to their <see cref="Application.Events.IEntityChangeHandler{TEntity}"/>s, e.g.
    /// <c>services.AddCoworkeeEvents(e => e.PublishChanges&lt;Order&gt;())</c>; <c>[PublishChanges]</c> on the entity does the same.
    /// </summary>
    public static IServiceCollection AddCoworkeeEvents(this IServiceCollection services, Action<EntityEventOptions> configure) =>
        services.Configure(configure);
}
