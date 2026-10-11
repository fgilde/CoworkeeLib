namespace Coworkee.Cli.Spec;

/// <summary>An entity a reference can point to: one of the spec, or one the app has already (<see cref="Spec"/> is null then).</summary>
public sealed record KnownEntity(string Module, string Name, string Plural, string? Display, EntitySpec? Spec = null);

/// <summary>The entities and enums of a spec, plus those of the app it extends, for resolving references.</summary>
public sealed class SpecIndex
{
    private readonly HashSet<string> _enums;

    public SpecIndex(AppSpec spec, IEnumerable<KnownEntity>? existing = null, IEnumerable<string>? existingEnums = null)
    {
        Spec = spec;
        Entities = [.. spec.Modules.SelectMany(m => m.Entities.Select(e => new KnownEntity(m.Name, e.Name, e.PluralName, DisplayOf(e)?.Name, e))), .. existing ?? []];
        _enums = new HashSet<string>([.. spec.Enums.Select(e => e.Name), .. existingEnums ?? []], StringComparer.Ordinal);
    }

    public static SpecIndex Empty { get; } = new(new AppSpec());

    public AppSpec Spec { get; }

    public IReadOnlyList<KnownEntity> Entities { get; }

    public bool HasEnum(string? name) => name is not null && _enums.Contains(name);

    /// <summary>The entity a reference of <paramref name="module"/> points to: "Customer" prefers the own module, "Crm.Customer" names the module.</summary>
    public KnownEntity? Target(string module, PropertySpec property)
    {
        if (property.Reference is not { Length: > 0 } reference)
        {
            return null;
        }

        var dot = reference.IndexOf('.');
        var (targetModule, name) = dot > 0 ? (reference[..dot], reference[(dot + 1)..]) : (null, reference);
        var matches = Entities.Where(e => e.Name == name && (targetModule is null || e.Module == targetModule)).ToList();
        return matches.FirstOrDefault(e => e.Module == module) ?? (matches.Count == 1 ? matches[0] : null);
    }

    /// <summary>The text property of the target shown for a reference, or null when the target has none (the id is shown).</summary>
    public string? ReferenceDisplay(string module, PropertySpec property) => property.ReferenceDisplay ?? Target(module, property)?.Display;

    /// <summary>References of spec entities with an inverse collection on the entity <paramref name="name"/> of <paramref name="module"/>.</summary>
    public IEnumerable<(EntitySpec Source, PropertySpec Property)> Inverses(string module, string name) =>
        Spec.Modules.Where(m => m.Name == module)
            .SelectMany(m => m.Entities.SelectMany(e => e.Properties.Where(p => p.Inverse is not null && Target(module, p) is { } t && t.Module == module && t.Name == name).Select(p => (e, p))));

    /// <summary>The display property: the one marked display, else the first string or email.</summary>
    public static PropertySpec? DisplayOf(EntitySpec entity) =>
        entity.Properties.FirstOrDefault(p => p.Display) ?? entity.Properties.FirstOrDefault(p => p.Type == PropertyType.String) ?? entity.Properties.FirstOrDefault(p => p.Type == PropertyType.Email);
}
