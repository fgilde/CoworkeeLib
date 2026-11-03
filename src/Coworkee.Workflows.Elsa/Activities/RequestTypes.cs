using System.Collections.Concurrent;
using System.Reflection;
using Coworkee.Application.Messaging;

namespace Coworkee.Workflows.Elsa.Activities;

/// <summary>Finds request types of the loaded assemblies by name and sends them through the dispatcher.</summary>
internal static class RequestTypes
{
    private static readonly ConcurrentDictionary<string, Type> Found = new(StringComparer.Ordinal);

    private static readonly MethodInfo Send = typeof(IDispatcher).GetMethod(nameof(IDispatcher.SendAsync))!;

    // ponytail: scans the loaded assemblies once per name; a registry if the app ever loads request assemblies late
    public static Type Find(string name) => Found.GetOrAdd(name, key => AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => !a.IsDynamic)
        .SelectMany(Types)
        .FirstOrDefault(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && (t.Name == key || t.FullName == key) && ResultOf(t) is not null)
        ?? throw new InvalidOperationException($"There is no request type {key}."));

    public static async Task<object?> SendAsync(IDispatcher dispatcher, object request, CancellationToken cancellationToken)
    {
        var task = (Task)Send.MakeGenericMethod(ResultOf(request.GetType())!).Invoke(dispatcher, [request, cancellationToken])!;
        await task;
        var result = task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task);
        return result switch
        {
            Core.Results.Result { IsSuccess: false } failed => throw new InvalidOperationException(failed.Error.Message),
            Core.Results.Result success => success.GetType().GetProperty("Value")?.GetValue(success),
            _ => result,
        };
    }

    private static Type? ResultOf(Type type) =>
        type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))?.GetGenericArguments()[0];

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
