using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddRequestMiddleware<TMiddleware>(this IServiceCollection services)
        where TMiddleware : class, IRequestMiddleware =>
        services.AddScoped<IRequestMiddleware, TMiddleware>();

    public static IServiceCollection AddRequestBehavior(this IServiceCollection services, Type openBehavior)
    {
        if (!openBehavior.IsGenericTypeDefinition || openBehavior.GetGenericArguments().Length != 2)
        {
            throw new ArgumentException($"{openBehavior.Name} must be an open generic type with the parameters TRequest and TResult.", nameof(openBehavior));
        }

        return services.AddScoped(typeof(IRequestBehavior<,>), openBehavior);
    }

    public static IServiceCollection AddRequestBehavior<TRequest, TResult, TBehavior>(this IServiceCollection services)
        where TRequest : IRequest<TResult>
        where TBehavior : class, IRequestBehavior<TRequest, TResult> =>
        services.AddScoped<IRequestBehavior<TRequest, TResult>, TBehavior>();
}
