using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;

namespace Coworkee.Ai;

/// <summary>
/// The tools of the application: the ones registered with AddAiTool plus every request of the loaded modules that carries
/// <see cref="AiToolAttribute"/> or <see cref="RequiresPermissionAttribute"/> and returns a <see cref="Result"/>.
/// </summary>
public sealed partial class AiToolCatalog
{
    public AiToolCatalog(IEnumerable<AiTool> registered, IReadOnlyList<CoworkeeModule> modules)
    {
        var tools = registered.ToList();
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var types = tools.Select(t => t.RequestType).ToHashSet();
        foreach (var tool in modules.Select(m => m.GetType().Assembly).Distinct().SelectMany(Discover))
        {
            if (!types.Contains(tool.RequestType) && names.Add(tool.Name))
            {
                tools.Add(tool);
            }
        }

        Tools = tools;
    }

    public IReadOnlyList<AiTool> Tools { get; }

    private static IEnumerable<AiTool> Discover(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            var marker = type.GetCustomAttribute<AiToolAttribute>();
            if (marker?.Exclude == true || (marker is null && type.GetCustomAttribute<RequiresPermissionAttribute>() is null))
            {
                continue;
            }

            var result = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))?.GetGenericArguments()[0];
            if (result is null || !typeof(Result).IsAssignableFrom(result) || HasStreams(type))
            {
                continue;
            }

            var name = marker?.Name ?? SnakeCase(Suffix().Replace(type.Name, string.Empty));
            var description = marker?.Description ?? type.GetCustomAttribute<DescriptionAttribute>()?.Description ?? Humanize(name);
            yield return new AiTool(name, description, type, result);
        }
    }

    // binary content does not travel as JSON tool input
    private static bool HasStreams(Type type) =>
        type.GetProperties().Any(p => typeof(Stream).IsAssignableFrom(p.PropertyType) || p.PropertyType.Name is "IFormFile" or "IBrowserFile");

    private static string SnakeCase(string name) => Words().Replace(name, "_$1").ToLowerInvariant();

    private static string Humanize(string name)
    {
        var text = name.Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    [GeneratedRegex("(Command|Query)$")]
    private static partial Regex Suffix();

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex Words();
}
