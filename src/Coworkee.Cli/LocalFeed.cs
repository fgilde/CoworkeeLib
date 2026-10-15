using System.Xml.Linq;

namespace Coworkee.Cli;

/// <summary>
/// A template from a local build (--template-source) belongs to Coworkee packages of the same build that nuget.org does not have:
/// the folder of the template (build/pack-local.ps1 packs everything into one folder) becomes a package source of the app.
/// </summary>
public static class LocalFeed
{
    public const string Name = "coworkee-local";

    /// <summary>The folder of a .nupkg or the folder given, when it holds Coworkee packages besides the template; otherwise null.</summary>
    public static string? Of(string? templateSource)
    {
        if (string.IsNullOrWhiteSpace(templateSource))
        {
            return null;
        }

        var full = Path.GetFullPath(templateSource);
        var folder = File.Exists(full) ? Path.GetDirectoryName(full) : Directory.Exists(full) ? full : null;
        return folder is not null && Directory.EnumerateFiles(folder, "Coworkee.*.nupkg").Any(f => !Path.GetFileName(f).StartsWith("Coworkee.Templates.", StringComparison.OrdinalIgnoreCase))
            ? folder
            : null;
    }

    /// <summary>Adds the feed to the app's nuget.config once.</summary>
    public static void Add(string appFolder, string feed)
    {
        var file = Path.Combine(appFolder, "nuget.config");
        var document = File.Exists(file) ? XDocument.Load(file) : new XDocument(new XElement("configuration"));
        var sources = document.Root!.Element("packageSources") ?? new XElement("packageSources");
        if (sources.Parent is null)
        {
            document.Root.Add(sources);
        }

        if (sources.Elements("add").Any(e => string.Equals((string?)e.Attribute("value"), feed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        sources.Add(new XElement("add", new XAttribute("key", Name), new XAttribute("value", feed)));
        document.Save(file);
    }

    /// <summary>The local folders among the package sources of the app's nuget.config.</summary>
    public static IEnumerable<string> Folders(string appFolder)
    {
        var file = Path.Combine(appFolder, "nuget.config");
        return File.Exists(file)
            ? XDocument.Load(file).Descendants("packageSources").Elements("add").Select(e => (string?)e.Attribute("value")).OfType<string>().Where(Directory.Exists)
            : [];
    }
}
