using System.Text.RegularExpressions;

namespace Coworkee.Cli.Spec;

public static partial class SpecValidator
{
    public const string ColorPattern = "^#[0-9a-fA-F]{6}$";

    private static readonly string[] Projects = ["Api", "AppHost", "Application", "Auth", "Contracts", "Infrastructure", "Migrations", "ServiceDefaults", "Web", "Catalog", "Documents"];
    private static readonly string[] SampleEntities = ["Brand", "Product", "Document", "DocumentType"];
    private static readonly string[] BaseProperties = ["Id", "TenantId", "CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy"];
    private static readonly string[] Images = [".svg", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".ico"];
    private static readonly string[] Videos = [".mp4", ".webm", ".ogg"];

    /// <summary>The problems of a spec, empty when it can be created; with <paramref name="baseDirectory"/> local logo and media files must exist.</summary>
    public static IReadOnlyList<string> Validate(AppSpec spec, string? baseDirectory = null) => Validate(spec, baseDirectory, []);

    /// <summary>
    /// Like <see cref="Validate(AppSpec, string?)"/>; logo and media files must lie below <paramref name="baseDirectory"/>
    /// unless they are in <paramref name="trustedFiles"/> (paths a person typed on the command line).
    /// </summary>
    public static IReadOnlyList<string> Validate(AppSpec spec, string? baseDirectory, IReadOnlyCollection<string> trustedFiles) => Validate(spec, baseDirectory, trustedFiles, new SpecIndex(spec));

    /// <summary>Like <see cref="Validate(AppSpec, string?, IReadOnlyCollection{string})"/>; references and enums may also point to what <paramref name="index"/> knows of the app.</summary>
    public static IReadOnlyList<string> Validate(AppSpec spec, string? baseDirectory, IReadOnlyCollection<string> trustedFiles, SpecIndex index)
    {
        var errors = new List<string>();
        Name(errors, "name", spec.Name);
        if (spec.Output is { } output && !IsRelativeInside(output))
        {
            errors.Add($"output: '{output}' must be a relative folder without '..'.");
        }

        foreach (var id in spec.Options.Keys.Where(id => OptionCatalog.Find(id) is null))
        {
            errors.Add($"options.{id}: unknown option, see coworkee options.");
        }

        var values = SpecOptions.Resolve(spec);
        foreach (var option in OptionCatalog.All.Where(o => o.Kind != OptionKind.File))
        {
            Value(errors, option, values[option.Id]);
        }

        foreach (var option in OptionCatalog.All.Where(o => o.Kind == OptionKind.File && SpecOptions.IsSet(spec, o.Id)))
        {
            if (values[option.Id] is { } file && FileProblem(option.Id, file, baseDirectory, trustedFiles.Contains(file)) is { } problem)
            {
                errors.Add($"{option.Id}: '{file}' {problem}.");
            }
        }

        var title = values["title"] ?? spec.Name;
        if (!IsSafeTitle(title))
        {
            errors.Add($"title: '{title}' must not contain quotes, backslashes, <, >, @ or control characters; it goes into code and HTML.");
        }

        if ((values["primaryColor"] ?? values["secondaryColor"]) is not null
            && OptionCatalog.Themes.Any(t => t.Equals(title, StringComparison.OrdinalIgnoreCase) || (t == "HighContrast" && title.Equals("High Contrast", StringComparison.OrdinalIgnoreCase))))
        {
            errors.Add($"branding.title: '{title}' names a built-in theme; the brand theme is named like the title.");
        }

        if (values["clientEntities"] == "false" && spec.Modules.SelectMany(m => m.Entities).Any(e => e.ClientEntity))
        {
            errors.Add("options.clientEntities: an entity is a client entity, so the option cannot be off.");
        }

        Languages(errors, values);
        Enums(errors, spec);
        Modules(errors, spec, values["samples"] == "true", index);
        return errors;
    }

    public static bool IsSimpleName(string? name) => name is not null && SimpleName().IsMatch(name);

    /// <summary>A title that can go into C# string literals, Razor and HTML as it is.</summary>
    public static bool IsSafeTitle(string title) => !title.Any(c => c is '"' or '\\' or '<' or '>' or '@' || char.IsControl(c));

    /// <summary>
    /// Why a logo (<paramref name="id"/> "logo") or sign-in media file cannot be used, or null: an image (media also a video or an http(s) address),
    /// existing below <paramref name="baseDirectory"/>; a <paramref name="trusted"/> path may be anywhere.
    /// </summary>
    public static string? FileProblem(string id, string value, string? baseDirectory, bool trusted)
    {
        if (id == "signInMedia" && IsWebAddress(value))
        {
            return null;
        }

        var extension = Path.GetExtension(value).ToLowerInvariant();
        if (!Images.Contains(extension) && !(id == "signInMedia" && Videos.Contains(extension)))
        {
            return id == "signInMedia" ? "is no image, video (mp4, webm, ogg) or http(s) address" : "is no image (svg, png, jpg, jpeg, webp, gif, ico)";
        }

        if (!trusted && (!IsRelativeInside(value) || (baseDirectory is not null && !IsUnder(baseDirectory, Path.Combine(baseDirectory, value)))))
        {
            return "must be a relative path inside the folder of the spec";
        }

        return baseDirectory is not null && !File.Exists(Path.Combine(baseDirectory, value)) ? "is no existing file" : null;
    }

    /// <summary>Whether <paramref name="path"/> resolves to <paramref name="directory"/> or below.</summary>
    public static bool IsUnder(string directory, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return (Path.GetFullPath(path) + Path.DirectorySeparatorChar).StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsRelativeInside(string path) =>
        !Path.IsPathRooted(path) && !path.Split('/', '\\').Contains("..");

    private static void Value(List<string> errors, CliOption option, string? value)
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
            OptionKind.Multi when SpecOptions.List(value) is var items && (items.Count == 0 || items.Distinct().Count() != items.Count || items.Any(i => option.Choices!.All(c => c.Value != i))) =>
                "a comma separated list without repeats of " + string.Join(", ", option.Choices!.Select(c => c.Value)),
            _ => null,
        };
        if (problem is not null)
        {
            errors.Add($"{option.Id}: '{value}' is not {problem}.");
        }
    }

    private static void Languages(List<string> errors, IReadOnlyDictionary<string, string?> values)
    {
        if (values["languages"] is { } languages && values["defaultLanguage"] is { } language && !SpecOptions.List(languages).Contains(language))
        {
            errors.Add($"defaultLanguage: '{language}' is not one of the languages {languages}.");
        }
    }

    public static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static void Name(List<string> errors, string path, string? name)
    {
        if (!IsSimpleName(name))
        {
            errors.Add($"{path}: '{name}' is not a simple name: start with a letter, then letters or digits only.");
        }
        else if (Keywords.Contains(name!))
        {
            errors.Add($"{path}: '{name}' is a C# keyword.");
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex SimpleName();

    [GeneratedRegex(ColorPattern)]
    private static partial Regex Color();
}
