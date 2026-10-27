using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.ExtendedAttributes;

public static class ExtendedAttributeServiceCollectionExtensions
{
    /// <summary>
    /// Lets <typeparamref name="TEntity"/> carry extended attributes under <paramref name="entityType"/>, e.g. "Documents". Attributes belong to a row
    /// by its Guid <c>Id</c>; an entity without one (an OData set keyed otherwise, e.g. by a code) is refused.
    /// </summary>
    public static IServiceCollection AddExtendedAttributes<TEntity>(this IServiceCollection services, string entityType, string viewPermission, string editPermission)
        where TEntity : class
    {
        if (typeof(TEntity).GetProperty("Id")?.PropertyType != typeof(Guid))
        {
            throw new InvalidOperationException($"Extended attributes need a Guid Id property on {typeof(TEntity).Name}; entities keyed otherwise cannot carry them.");
        }

        return services.AddSingleton(ExtendedAttributeRegistration.For<TEntity>(entityType, viewPermission, editPermission));
    }
}
