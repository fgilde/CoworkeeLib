using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Customization;

public sealed class ComponentReplacementOptions
{
    private readonly Dictionary<Type, Type> _replacements = [];

    public IReadOnlyDictionary<Type, Type> Replacements => _replacements;

    public ComponentReplacementOptions Replace<TOriginal, TReplacement>()
        where TOriginal : IComponent
        where TReplacement : IComponent
    {
        if (Resolve(typeof(TReplacement)) == typeof(TOriginal))
        {
            throw new InvalidOperationException($"Replacing {typeof(TOriginal).Name} with {typeof(TReplacement).Name} would replace it with itself.");
        }

        _replacements[typeof(TOriginal)] = typeof(TReplacement);
        return this;
    }

    public Type Resolve(Type componentType) =>
        _replacements.TryGetValue(componentType, out var replacement) ? Resolve(replacement) : componentType;
}
