using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Application.Retention;

public static class RetentionExtensions
{
    /// <summary>The id of the nightly recurring job of the Retention module; modules with an own cleanup skip it when this job is registered.</summary>
    public const string JobId = "coworkee-retention";

    public static IServiceCollection AddRetentionTarget<T>(this IServiceCollection services) where T : class, IRetentionTarget
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRetentionTarget, T>());
        return services;
    }

    /// <summary>Soft deleted rows of <typeparamref name="T"/> are left to another target (files with their blobs), not to "soft-deleted".</summary>
    public static IServiceCollection AddRetentionOwnedType<T>(this IServiceCollection services) =>
        services.AddSingleton(new RetentionOwnedType(typeof(T)));
}
