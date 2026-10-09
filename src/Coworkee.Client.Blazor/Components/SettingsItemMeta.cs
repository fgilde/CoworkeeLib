using System.Collections;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// MudEx asks every object form for an IObjectMetaConfiguration of its type; objects nested in a settings section
/// (a list item MudEx edits in its own dialog) get the settings form's rules, every other form stays as it is.
/// </summary>
public sealed class SettingsItemMeta<T>(IEnumerable<ClientAppConfiguration> sections) : IObjectMetaConfiguration<T>
{
    public Task ConfigureAsync(ObjectEditMeta<T> meta)
    {
        if (Applies(sections))
        {
            SettingsFormRules.Apply(meta);
        }

        return Task.CompletedTask;
    }

    public static bool Applies(IEnumerable<ClientAppConfiguration> sections) => sections.Any(s => Reaches(s.Type, typeof(T), []));

    private static bool Reaches(Type from, Type target, HashSet<Type> seen) =>
        from == target || (seen.Add(from) && Nested(from).Any(t => Reaches(t, target, seen)));

    private static IEnumerable<Type> Nested(Type type) =>
        type.IsPrimitive || type == typeof(string) || type.IsEnum ? []
        : type.IsArray ? [type.GetElementType()!]
        : type.IsGenericType && type.IsAssignableTo(typeof(IEnumerable)) ? type.GetGenericArguments()
        : type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true ? []
        : type.GetProperties().Select(p => p.PropertyType);
}
