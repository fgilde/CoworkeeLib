using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Coworkee.SourceGenerators;

/// <summary>The naming conventions of application services; server and client derive the same routes from them.</summary>
internal static class Naming
{
    private static readonly string[] ServiceSuffixes = ["ApplicationService", "AppService", "Service"];

    private static readonly (string Prefix, string Method, bool Strip)[] Verbs =
    [
        ("Get", "GET", true),
        ("Find", "GET", false),
        ("List", "GET", false),
        ("Search", "GET", false),
        ("Query", "GET", false),
        ("Count", "GET", false),
        ("Create", "POST", true),
        ("Add", "POST", true),
        ("Insert", "POST", true),
        ("Update", "PUT", true),
        ("Patch", "PATCH", true),
        ("Delete", "DELETE", true),
        ("Remove", "DELETE", true),
    ];

    private static readonly HashSet<string> SilentRests = new(StringComparer.Ordinal) { string.Empty, "List", "All", "ById" };

    /// <summary>IDocumentTypeAppService becomes DocumentType.</summary>
    public static string ServiceName(string interfaceName)
    {
        var name = interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1]) ? interfaceName.Substring(1) : interfaceName;
        var suffix = ServiceSuffixes.FirstOrDefault(s => name.EndsWith(s, StringComparison.Ordinal) && name.Length > s.Length);
        return suffix is null ? name : name.Substring(0, name.Length - suffix.Length);
    }

    /// <summary>DocumentType becomes /api/v1/document-types.</summary>
    public static string Route(string serviceName)
    {
        var words = PluralWords(serviceName);
        return "/api/v1/" + string.Join("-", words.Select(w => w.ToLowerInvariant()));
    }

    /// <summary>DocumentType becomes "Document types".</summary>
    public static string Tag(string serviceName)
    {
        var words = PluralWords(serviceName);
        return string.Join(" ", words.Select((w, i) => i == 0 ? w : w.ToLowerInvariant()));
    }

    /// <summary>The HTTP method of a method name (without Async) and the rest of the name that becomes the route.</summary>
    public static (string Method, string Remainder) Operation(string name)
    {
        foreach (var (prefix, method, strip) in Verbs)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) && (name.Length == prefix.Length || char.IsUpper(name[prefix.Length])))
            {
                var rest = strip ? name.Substring(prefix.Length) : name;
                return (method, SilentRests.Contains(rest) ? string.Empty : rest);
            }
        }

        return ("POST", name);
    }

    public static string Kebab(string name) => string.Join("-", Words(name).Select(w => w.ToLowerInvariant()));

    public static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static List<string> PluralWords(string name)
    {
        var words = Words(name);
        if (words.Count > 0)
        {
            words[words.Count - 1] = Plural(words[words.Count - 1]);
        }

        return words;
    }

    private static string Plural(string word)
    {
        if (word.EndsWith("y", StringComparison.OrdinalIgnoreCase) && word.Length > 1 && "aeiou".IndexOf(char.ToLowerInvariant(word[word.Length - 2])) < 0)
        {
            return word.Substring(0, word.Length - 1) + "ies";
        }

        return new[] { "s", "x", "z", "ch", "sh" }.Any(e => word.EndsWith(e, StringComparison.OrdinalIgnoreCase)) ? word + "es" : word + "s";
    }

    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var boundary = current.Length > 0 && char.IsUpper(c)
                && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
            if (boundary)
            {
                words.Add(current.ToString());
                current.Clear();
            }

            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}
