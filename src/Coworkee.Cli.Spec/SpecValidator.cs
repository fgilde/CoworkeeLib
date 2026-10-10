using System.Text.RegularExpressions;

namespace Coworkee.Cli.Spec;

public static partial class SpecValidator
{
    public const string ColorPattern = "^#[0-9a-fA-F]{6}$";

    private static readonly string[] Projects = ["Api", "AppHost", "Application", "Auth", "Contracts", "Infrastructure", "Migrations", "ServiceDefaults", "Web", "Catalog", "Documents"];
    private static readonly string[] SampleEntities = ["Brand", "Product", "Document", "DocumentType"];
    private static readonly string[] BaseProperties = ["Id", "TenantId", "CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy"];

    /// <summary>The problems of a spec, empty when it can be created; with <paramref name="baseDirectory"/> local logo and media files must exist.</summary>
    public static IReadOnlyList<string> Validate(AppSpec spec, string? baseDirectory = null)
    {
        var errors = new List<string>();
        Name(errors, "name", spec.Name);
        foreach (var id in spec.Options.Keys.Where(id => OptionCatalog.Find(id) is null))
        {
            errors.Add($"options.{id}: unknown option, see coworkee options.");
        }

        var values = SpecOptions.Resolve(spec);
        foreach (var option in OptionCatalog.All)
        {
            Value(errors, option, values[option.Id], baseDirectory);
        }

        var title = values["title"] ?? spec.Name;
        if ((values["primaryColor"] ?? values["secondaryColor"]) is not null
            && OptionCatalog.Themes.Any(t => t.Equals(title, StringComparison.OrdinalIgnoreCase) || (t == "HighContrast" && title.Equals("High Contrast", StringComparison.OrdinalIgnoreCase))))
        {
            errors.Add($"branding.title: '{title}' names a built-in theme; the brand theme is named like the title.");
        }

        if (values["clientEntities"] == "false" && spec.Modules.SelectMany(m => m.Entities).Any(e => e.ClientEntity))
        {
            errors.Add("options.clientEntities: an entity is a client entity, so the option cannot be off.");
        }

        Modules(errors, spec, values["samples"] == "true");
        return errors;
    }

    public static bool IsSimpleName(string? name) => name is not null && SimpleName().IsMatch(name);

    private static void Value(List<string> errors, CliOption option, string? value, string? baseDirectory)
    {
        if (value is null)
        {
            return;
        }

        var problem = option.Kind switch
        {
            OptionKind.Bool when value is not ("true" or "false") => "true or false",
            OptionKind.Choice when !option.Choices!.Any(c => c.Value == value) => string.Join(", ", option.Choices!.Select(c => c.Value)),
            OptionKind.Color when !Color().IsMatch(value) => "a color #rrggbb",
            OptionKind.File when baseDirectory is not null && option.Default != value && !IsWebAddress(value) && !File.Exists(Path.Combine(baseDirectory, value)) => "an existing file",
            _ => null,
        };
        if (problem is not null)
        {
            errors.Add($"{option.Id}: '{value}' is not {problem}.");
        }
    }

    public static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static void Modules(List<string> errors, AppSpec spec, bool samples)
    {
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (module, index) in spec.Modules.Select((m, i) => (m, i)))
        {
            var path = $"modules[{index}]";
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
                Entity(errors, $"{path}.entities[{entityIndex}]", entity, entities, samples);
            }
        }
    }

    private static void Entity(List<string> errors, string path, EntitySpec entity, HashSet<string> taken, bool samples)
    {
        Name(errors, path + ".name", entity.Name);
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

        foreach (var (property, index) in entity.Properties.Select((p, i) => (p, i)))
        {
            var at = $"{path}.properties[{index}]";
            Name(errors, at + ".name", property.Name);
            if (BaseProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase) || property.Name == entity.Name)
            {
                errors.Add($"{at}.name: '{property.Name}' is taken by the entity itself.");
            }

            if (entity.Properties.Count(p => p.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                errors.Add($"{at}.name: '{property.Name}' appears twice.");
            }

            if (property.MaxLength is { } max && (property.Type != PropertyType.String || max < 1))
            {
                errors.Add($"{at}.maxLength: only strings have a maximum length, and it is at least 1.");
            }
        }
    }

    private static void Name(List<string> errors, string path, string? name)
    {
        if (!IsSimpleName(name))
        {
            errors.Add($"{path}: '{name}' is not a simple name: start with a letter, then letters or digits only.");
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex SimpleName();

    [GeneratedRegex(ColorPattern)]
    private static partial Regex Color();
}
