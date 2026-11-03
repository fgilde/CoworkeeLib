using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

public static partial class EmbeddedTemplates
{
    public static IReadOnlyDictionary<string, MailTemplateContent> Load(Assembly assembly, string name)
    {
        var defaults = new Dictionary<string, MailTemplateContent>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var match = ResourceName().Match(resource);
            if (!match.Success || match.Groups["name"].Value != name || match.Groups["part"].Value != "body")
            {
                continue;
            }

            var culture = match.Groups["culture"].Value;
            var subjectResource = resource[..^"body.scriban".Length] + "subject.scriban";
            defaults[culture] = new MailTemplateContent(
                assembly.GetManifestResourceNames().Contains(subjectResource) ? Read(assembly, subjectResource).Trim() : string.Empty,
                Read(assembly, resource));
        }

        return defaults.Count > 0 ? defaults : throw new InvalidOperationException($"No embedded mail template '{name}' in {assembly.GetName().Name}.");
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\.Templates\.(?<name>.+)\.(?<culture>[a-z]{2}(-[A-Z]{2})?)\.(?<part>subject|body)\.scriban$")]
    private static partial Regex ResourceName();
}
