using System.Globalization;

namespace Coworkee.Cli.Spec;

public static partial class SpecValidator
{
    private const int MaxLabelLength = 100;

    // types the generated code uses unqualified; an entity or enum of that name would shadow them
    internal static readonly string[] ReservedTypes =
    [
        "Task", "Guid", "String", "Uri", "Result", "Error", "Color", "Severity", "Icons", "Description", "DateOnly", "TimeOnly", "DateTimeOffset",
        "Enum", "Object", "Action", "Func", "List", "Dictionary", "Math", "Type", "Exception", "Console", "Environment", "Program",
    ];

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while", "record", "var", "dynamic", "value",
    };

    private static readonly PropertyType[] TextTypes = [PropertyType.String, PropertyType.Email, PropertyType.Url];
    private static readonly PropertyType[] SearchTypes = [PropertyType.String, PropertyType.Text, PropertyType.Email, PropertyType.Url];
    private static readonly PropertyType[] FacetTypes = [PropertyType.Bool, PropertyType.Enum, PropertyType.Reference, PropertyType.Int, PropertyType.Long, PropertyType.String];

    private static void Enums(List<string> errors, AppSpec spec)
    {
        if (spec.Enums.Count > 0 && spec.Modules.Count == 0)
        {
            errors.Add("enums: enums are generated with the modules; add a module whose entities use them.");
        }

        foreach (var (item, index) in spec.Enums.Select((e, i) => (e, i)))
        {
            var path = $"enums[{index}]";
            TypeName(errors, path + ".name", item.Name);
            if (spec.Enums.Count(e => e.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) > 1
                || spec.Modules.SelectMany(m => m.Entities).Any(e => e.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"{path}.name: '{item.Name}' appears twice; enum and entity names are unique in the app.");
            }

            if (item.Values.Count == 0)
            {
                errors.Add($"{path}.values: an enum needs at least one value.");
            }

            foreach (var (value, valueIndex) in item.Values.Select((v, i) => (v, i)))
            {
                Name(errors, $"{path}.values[{valueIndex}]", value);
                if (item.Values.Count(v => v.Equals(value, StringComparison.OrdinalIgnoreCase)) > 1)
                {
                    errors.Add($"{path}.values[{valueIndex}]: '{value}' appears twice.");
                }
            }
        }
    }

    private static void Modules(List<string> errors, AppSpec spec, bool samples, SpecIndex index)
    {
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (module, moduleIndex) in spec.Modules.Select((m, i) => (m, i)))
        {
            var path = $"modules[{moduleIndex}]";
            Name(errors, path + ".name", module.Name);
            if (Projects.Contains(module.Name, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{path}.name: '{module.Name}' is taken by a project of the app.");
            }

            if (spec.Modules.Count(m => m.Name.Equals(module.Name, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                errors.Add($"{path}.name: '{module.Name}' appears twice.");
            }

            if (module.Entities.Count == 0)
            {
                errors.Add($"{path}.entities: a module needs at least one entity.");
            }

            foreach (var (entity, entityIndex) in module.Entities.Select((e, i) => (e, i)))
            {
                Entity(errors, $"{path}.entities[{entityIndex}]", module.Name, entity, entities, samples, index);
            }
        }

        if (ModuleCycle(spec, index) is { } cyclic)
        {
            errors.Add($"modules: {cyclic} references a module that references it back; module references must not go round.");
        }
    }

    // ponytail: only cycles among the modules of the spec; a cycle through a module the app has already is caught by the CLI for direct pairs only
    private static string? ModuleCycle(AppSpec spec, SpecIndex index)
    {
        var edges = spec.Modules.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.SelectMany(m => m.Entities).SelectMany(e => e.Properties)
            .Select(p => index.Target(g.Key, p)?.Module).OfType<string>().Where(t => t != g.Key).ToHashSet());
        foreach (var start in edges.Keys)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<string>(edges[start]);
            while (stack.TryPop(out var next))
            {
                if (next == start)
                {
                    return start;
                }

                if (seen.Add(next) && edges.TryGetValue(next, out var more))
                {
                    foreach (var target in more)
                    {
                        stack.Push(target);
                    }
                }
            }
        }

        return null;
    }

    private static void Entity(List<string> errors, string path, string module, EntitySpec entity, HashSet<string> taken, bool samples, SpecIndex index)
    {
        TypeName(errors, path + ".name", entity.Name);
        if (entity.Plural is not null)
        {
            Name(errors, path + ".plural", entity.Plural);
        }

        if (!taken.Add(entity.Name) | !taken.Add("plural:" + entity.PluralName))
        {
            errors.Add($"{path}.name: '{entity.Name}' appears twice; entity names are unique in the app.");
        }

        if (samples && SampleEntities.Contains(entity.Name, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"{path}.name: '{entity.Name}' is an entity of the samples.");
        }

        if (entity.Properties.Count == 0)
        {
            errors.Add($"{path}.properties: an entity needs at least one property.");
        }

        if (entity.Properties.Count(p => p.Display) > 1)
        {
            errors.Add($"{path}.properties: only one property can be the display property.");
        }

        var members = entity.Properties.SelectMany(p => p.Type == PropertyType.Reference ? [p.Name, p.Name + "Id", p.Name + "Reference"] : new[] { p.Name })
            .Concat(index.Inverses(module, entity.Name).Select(i => i.Property.Inverse!)).ToList();
        foreach (var (property, propertyIndex) in entity.Properties.Select((p, i) => (p, i)))
        {
            var at = $"{path}.properties[{propertyIndex}]";
            Name(errors, at + ".name", property.Name);
            if (BaseProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase) || property.Name == entity.Name)
            {
                errors.Add($"{at}.name: '{property.Name}' is taken by the entity itself.");
            }

            if (members.Count(m => m.Equals(property.Name, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                errors.Add($"{at}.name: '{property.Name}' appears twice (a reference also takes <name>Id, an inverse collection its name).");
            }

            Property(errors, at, module, property, index);
        }
    }

    private static void Property(List<string> errors, string at, string module, PropertySpec property, SpecIndex index)
    {
        var type = property.Type;
        Check(errors, at + ".maxLength", property.MaxLength is { } max && (!TextTypes.Contains(type) || max < 1), "only strings, emails and urls have a maximum length, and it is at least 1");
        Check(errors, at + ".precision", (property.Precision ?? property.Scale) is not null && type is not (PropertyType.Decimal or PropertyType.Money), "only decimals and money have a precision and scale");
        Check(errors, at + ".precision", property.Precision is < 1 or > 38 || property.Scale < 0 || property.Scale > (property.Precision ?? 18), "precision is 1 to 38, scale 0 to the precision");
        Check(errors, at + ".enum", (type == PropertyType.Enum) != (property.Enum is not null), "type enum and enum go together");
        Check(errors, at + ".enum", type == PropertyType.Enum && property.Enum is not null && !index.HasEnum(property.Enum), $"'{property.Enum}' is no enum of the spec");
        Check(errors, at + ".reference", (type == PropertyType.Reference) != (property.Reference is not null), "type reference and reference go together");
        Check(errors, at, type != PropertyType.Reference && (property.ReferenceDisplay is not null || property.Inverse is not null || property.OnDelete is not null),
            "referenceDisplay, onDelete and inverse belong to references");
        if (type == PropertyType.Reference && property.Reference is not null)
        {
            Reference(errors, at, module, property, index);
        }

        Check(errors, at + ".unique", property.Unique && type is PropertyType.Text or PropertyType.Bool, "long texts and bools cannot be unique");
        Check(errors, at + ".display", property.Display && type is not (PropertyType.String or PropertyType.Email), "only a string or email can be the display property");
        Check(errors, at + ".searchable", property.Searchable == true && !SearchTypes.Contains(type), "only texts, emails and urls are searchable");
        Check(errors, at + ".facet", property.Facet && !FacetTypes.Contains(type), "facets are bools, enums, references, whole numbers or strings");
        if (property.Label is { } label && (label.Length is 0 or > MaxLabelLength || !IsSafeTitle(label)))
        {
            errors.Add($"{at}.label: '{label}' must have 1 to {MaxLabelLength} characters without quotes, backslashes, <, >, @ or control characters.");
        }

        if (property.Default is { } value && DefaultProblem(property, value, index) is { } problem)
        {
            errors.Add($"{at}.default: '{value}' {problem}.");
        }
    }

    private static void Reference(List<string> errors, string at, string module, PropertySpec property, SpecIndex index)
    {
        var target = index.Target(module, property);
        if (target is null)
        {
            errors.Add($"{at}.reference: '{property.Reference}' is no entity of the spec or the app (Name or Module.Name).");
            return;
        }

        Check(errors, at + ".onDelete", property is { OnDelete: DeleteBehavior.SetNull, Required: true }, "setNull needs an optional reference");
        Check(errors, at + ".referenceDisplay", (property.ReferenceDisplay ?? target.Display) is null, $"{target.Name} has no string or email to show in lists and pickers; name one with referenceDisplay");
        if (property.ReferenceDisplay is { } display)
        {
            Name(errors, at + ".referenceDisplay", display);
            Check(errors, at + ".referenceDisplay", target.Spec is { } spec && !spec.Properties.Any(p => p.Name == display && p.Type is PropertyType.String or PropertyType.Email),
                $"'{display}' is no string or email of {target.Name}");
        }

        if (property.Inverse is not { } inverse)
        {
            return;
        }

        Name(errors, at + ".inverse", inverse);
        Check(errors, at + ".inverse", target.Spec is null || target.Module != module, "inverse collections need a target of the same module in the spec");
        Check(errors, at + ".inverse", target.Spec is { } owner && (inverse == target.Name || BaseProperties.Contains(inverse, StringComparer.OrdinalIgnoreCase)
            || owner.Properties.Any(p => p.Name.Equals(inverse, StringComparison.OrdinalIgnoreCase) || (p.Type == PropertyType.Reference && (p.Name + "Id").Equals(inverse, StringComparison.OrdinalIgnoreCase)))
            || index.Inverses(module, target.Name).Count(i => i.Property.Inverse!.Equals(inverse, StringComparison.OrdinalIgnoreCase)) > 1),
            $"'{inverse}' is taken on {target.Name}");
    }

    private static string? DefaultProblem(PropertySpec property, string value, SpecIndex index) => property.Type switch
    {
        PropertyType.Int when !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) => "is no int",
        PropertyType.Long when !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) => "is no long",
        PropertyType.Decimal or PropertyType.Money when !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _) => "is no decimal like 12.5",
        PropertyType.Double when !double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _) => "is no number like 12.5",
        PropertyType.Bool when value is not ("true" or "false") => "is not true or false",
        PropertyType.Enum when !index.Spec.Enums.Any(e => e.Name == property.Enum && e.Values.Contains(value)) => $"is no value of {property.Enum}",
        PropertyType.String or PropertyType.Text or PropertyType.Email or PropertyType.Url when !IsSafeTitle(value) || value.Contains('{') || value.Contains('}') =>
            "must not contain quotes, backslashes, braces, <, >, @ or control characters",
        PropertyType.String or PropertyType.Email or PropertyType.Url when value.Length > (property.MaxLength ?? int.MaxValue) => "is longer than the maximum length",
        PropertyType.DateTime or PropertyType.DateOnly or PropertyType.TimeOnly or PropertyType.Guid or PropertyType.Reference => "cannot be a default; only numbers, bools, enums and texts have one",
        _ => null,
    };

    private static void TypeName(List<string> errors, string path, string? name)
    {
        Name(errors, path, name);
        if (ReservedTypes.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"{path}: '{name}' would hide a type the generated code uses.");
        }
    }

    private static void Check(List<string> errors, string path, bool failed, string message)
    {
        if (failed)
        {
            errors.Add($"{path}: {message}.");
        }
    }
}
