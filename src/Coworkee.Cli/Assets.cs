using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli;

/// <summary>Logo and sign-in media of <c>coworkee new</c>: local files go into the wwwroot of the web client, the template gets their address there.</summary>
internal static class Assets
{
    public static string TemplateValue(string id, string value) => id switch
    {
        "logo" => Path.GetFileName(value),
        "signInMedia" when !SpecValidator.IsWebAddress(value) => "/media/" + Path.GetFileName(value),
        _ => value,
    };

    public static void Copy(NewSettings settings, IReadOnlyDictionary<string, string?> values)
    {
        var wwwroot = Path.Combine(settings.Output, "src", $"{settings.Spec.Name}.Web.Client", "wwwroot");
        if (SpecOptions.IsSet(settings.Spec, "logo") && values["logo"] is { } logo)
        {
            Copy(settings, "logo", logo, wwwroot);
        }

        if (SpecOptions.IsSet(settings.Spec, "signInMedia") && OptionCatalog.IsActive(OptionCatalog.Find("signInMedia")!, values) && values["signInMedia"] is { } media && !SpecValidator.IsWebAddress(media))
        {
            Copy(settings, "signInMedia", media, Path.Combine(wwwroot, "media"));
        }
    }

    // checked again here: whatever lands in wwwroot is served to everyone
    private static void Copy(NewSettings settings, string id, string value, string folder)
    {
        if (SpecValidator.FileProblem(id, value, settings.BaseDirectory, settings.TrustedFiles?.Contains(value) == true) is { } problem)
        {
            throw new CliException($"{id}: '{value}' {problem}.");
        }

        var file = Path.Combine(settings.BaseDirectory, value);
        Directory.CreateDirectory(folder);
        File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
    }
}
