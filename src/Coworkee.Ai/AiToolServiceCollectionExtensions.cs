using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Ai;

public static class AiToolServiceCollectionExtensions
{
    /// <summary>Offers <typeparamref name="TRequest"/> as a tool. Names are lower snake case and unique; the request returns a <see cref="Result"/>.</summary>
    public static IServiceCollection AddAiTool<TRequest>(this IServiceCollection services, string name, string description)
        where TRequest : class
    {
        var resultType = typeof(TRequest).GetInterfaces()
            .SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))?.GetGenericArguments()[0]
            ?? throw new ArgumentException($"{typeof(TRequest).Name} is not a dispatcher request.", nameof(TRequest));
        if (!typeof(Result).IsAssignableFrom(resultType))
        {
            throw new ArgumentException($"{typeof(TRequest).Name} must return a Result, so failures reach the model as messages.", nameof(TRequest));
        }

        return services.AddSingleton(new AiTool(name, description, typeof(TRequest), resultType));
    }
}
