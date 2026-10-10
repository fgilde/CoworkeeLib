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
        if (values["logo"] is { } logo && logo != OptionCatalog.Find("logo")!.Default)
        {
            Copy(Path.Combine(settings.BaseDirectory, logo), wwwroot);
        }

        if (values["signInMedia"] is { } media && !SpecValidator.IsWebAddress(media))
        {
            Copy(Path.Combine(settings.BaseDirectory, media), Path.Combine(wwwroot, "media"));
        }
    }

    private static void Copy(string file, string folder)
    {
        Directory.CreateDirectory(folder);
        File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
    }
}
