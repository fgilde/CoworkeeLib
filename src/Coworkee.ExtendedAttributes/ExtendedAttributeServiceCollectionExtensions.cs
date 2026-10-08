using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.ExtendedAttributes;

public static class ExtendedAttributeServiceCollectionExtensions
{
    /// <summary>Lets <typeparamref name="TEntity"/> carry extended attributes under <paramref name="entityType"/>, e.g. "Documents".</summary>
    public static IServiceCollection AddExtendedAttributes<TEntity>(this IServiceCollection services, string entityType, string viewPermission, string editPermission)
        where TEntity : class =>
        services.AddSingleton(ExtendedAttributeRegistration.For<TEntity>(entityType, viewPermission, editPermission));
}
