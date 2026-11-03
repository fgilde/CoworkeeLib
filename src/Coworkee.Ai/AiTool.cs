using System.Reflection;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;

namespace Coworkee.Ai;

/// <summary>
/// A tool the assistant and MCP clients may call: a dispatcher request, so it runs with the permission, resource,
/// validation and unit of work checks of the API. The JSON input is deserialized into <see cref="RequestType"/>.
/// </summary>
public sealed record AiTool(string Name, string Description, Type RequestType, Type ResultType)
{
    public IReadOnlyList<string> Permissions { get; } = RequestType.GetCustomAttributes<RequiresPermissionAttribute>().Select(a => a.Permission).ToList();

    /// <summary>Queries only read; commands change data and run only on a direct user request (see <see cref="AiToolRunner.RunAsync"/>).</summary>
    public bool ReadOnly { get; } = !IsCommand(RequestType);

    /// <summary>Every command unless marked <see cref="AiToolAttribute.NonDestructive"/>; MCP clients see the hint, the assistant window highlights it.</summary>
    public bool Destructive { get; } = IsCommand(RequestType) && RequestType.GetCustomAttribute<AiToolAttribute>()?.NonDestructive != true;

    private static bool IsCommand(Type type) => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
}
