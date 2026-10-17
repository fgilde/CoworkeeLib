using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Coworkee.Cli.Apps;

/// <summary>A text or regex replacement in the files of the globs (relative to the solution), skipped in files that contain <see cref="Unless"/>.</summary>
internal sealed record RenameRule(string Find, string Replace, string[] Files, string? Unless = null, string? Description = null);

/// <summary>A line inserted after the first line matching <see cref="InsertAfter"/>, with that line's indentation; skipped in files that contain <see cref="Unless"/>.</summary>
internal sealed record InsertRule(string InsertAfter, string Line, string[] Files, string? Unless = null, string? Description = null);

/// <summary>A package or MSBuild SDK raised to <see cref="Version"/> where the app has an older one.</summary>
internal sealed record PackageRule(string Id, string Version);

/// <summary>A step the person takes; <see cref="Find"/> locates the code it concerns.</summary>
internal sealed record ManualRule(string Text, string Find, string[] Files, string? Unless = null);

internal sealed record VersionRules(string Version, string[]? Breaking, bool NeedsMigration, RenameRule[]? Renames, InsertRule[]? Insertions, PackageRule[]? Packages, ManualRule[]? Manual);

/// <summary>A change coworkee update made (or would make), or a place a manual step concerns.</summary>
internal sealed record UpgradeChange(string File, string Text, int Line = 0)
{
    public override string ToString() => Line > 0 ? $"{File}:{Line}: {Text}" : $"{File}: {Text}";
}

/// <summary>The rules of upgrade-rules.json (shipped inside the CLI): breaking changes, automatic renames and bumps, manual steps per version.</summary>
internal static class UpgradeRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    public static IReadOnlyList<VersionRules> All { get; } = Load();

    /// <summary>The versions after <paramref name="from"/> up to <paramref name="to"/>; prerelease builds count as their release.</summary>
    public static IReadOnlyList<VersionRules> Between(string? from, string to) =>
        [.. All.Where(r => (from is null || Compare(r.Version, from) > 0) && Compare(r.Version, to) <= 0).OrderBy(r => Parse(r.Version))];

    /// <summary>Applies (or with <paramref name="dryRun"/> only lists) the renames and package bumps.</summary>
    public static IReadOnlyList<UpgradeChange> Apply(string root, IEnumerable<VersionRules> versions, bool dryRun)
    {
        var changes = new List<UpgradeChange>();
        foreach (var version in versions)
        {
            foreach (var rename in version.Renames ?? [])
            {
                var regex = new Regex(rename.Find);
                foreach (var file in Files(root, rename.Files))
                {
                    var text = File.ReadAllText(Path.Combine(root, file));
                    if ((rename.Unless is not null && text.Contains(rename.Unless, StringComparison.Ordinal)) || regex.Matches(text) is not { Count: > 0 } matches)
                    {
                        continue;
                    }

                    changes.Add(new(file, rename.Description ?? $"{matches.Count}x {rename.Find} -> {rename.Replace}"));
                    Write(root, file, regex.Replace(text, rename.Replace), dryRun);
                }
            }

            foreach (var insertion in version.Insertions ?? [])
            {
                var regex = new Regex(insertion.InsertAfter);
                foreach (var file in Files(root, insertion.Files))
                {
                    var text = File.ReadAllText(Path.Combine(root, file));
                    if ((insertion.Unless is not null && text.Contains(insertion.Unless, StringComparison.Ordinal)) || regex.Match(text) is not { Success: true } match)
                    {
                        continue;
                    }

                    changes.Add(new(file, insertion.Description ?? $"+ {insertion.Line}"));
                    Write(root, file, Insert(text, match.Index, insertion.Line), dryRun);
                }
            }

            foreach (var package in version.Packages ?? [])
            {
                var reference = new Regex($@"(<PackageVersion\s+Include=""{Regex.Escape(package.Id)}""\s+Version="")([^""]+)("")|(Sdk=""{Regex.Escape(package.Id)}/)([^""]+)("")", RegexOptions.IgnoreCase);
                foreach (var file in Files(root, ["Directory.Packages.props", "**/*.csproj"]))
                {
                    var text = File.ReadAllText(Path.Combine(root, file));
                    string? old = null;
                    var updated = reference.Replace(text, m =>
                    {
                        var (prefix, current, suffix) = m.Groups[1].Success ? (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) : (m.Groups[4].Value, m.Groups[5].Value, m.Groups[6].Value);
                        if (Compare(current, package.Version) >= 0)
                        {
                            return m.Value;
                        }

                        old = current;
                        return prefix + package.Version + suffix;
                    });
                    if (old is not null)
                    {
                        changes.Add(new(file, $"{package.Id} {old} -> {package.Version}"));
                        Write(root, file, updated, dryRun);
                    }
                }
            }
        }

        return changes;
    }

    /// <summary>The places of the manual steps, with line numbers.</summary>
    public static IReadOnlyList<UpgradeChange> Manual(string root, ManualRule rule)
    {
        var regex = new Regex(rule.Find);
        var hits = new List<UpgradeChange>();
        foreach (var file in Files(root, rule.Files))
        {
            var lines = File.ReadAllLines(Path.Combine(root, file));
            if (rule.Unless is not null && lines.Any(l => l.Contains(rule.Unless, StringComparison.Ordinal)))
            {
                continue;
            }

            hits.AddRange(lines.Select((l, i) => (Line: l, Number: i + 1)).Where(l => regex.IsMatch(l.Line)).Select(l => new UpgradeChange(file, l.Line.Trim(), l.Number)));
        }

        return hits;
    }

    /// <summary>Inserts <paramref name="line"/> after the line holding <paramref name="index"/>, with its indentation and line ending.</summary>
    private static string Insert(string text, int index, string line)
    {
        var start = index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
        var indent = string.Concat(text[start..].TakeWhile(c => c is ' ' or '\t'));
        var end = text.IndexOf('\n', index);
        if (end < 0)
        {
            return $"{text}{Environment.NewLine}{indent}{line}";
        }

        var newline = end > 0 && text[end - 1] == '\r' ? "\r\n" : "\n";
        return text.Insert(end + 1, indent + line + newline);
    }

    public static int Compare(string a, string b) => Parse(a).CompareTo(Parse(b));

    private static Version Parse(string version) =>
        Version.TryParse(version.Split('-', '+')[0], out var parsed) ? parsed : new Version(0, 0);

    private static IEnumerable<string> Files(string root, IEnumerable<string> globs)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(globs);
        matcher.AddExcludePatterns(["**/bin/**", "**/obj/**", "**/node_modules/**", ".git/**"]);
        return matcher.GetResultsInFullPath(root).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal);
    }

    private static void Write(string root, string file, string text, bool dryRun)
    {
        if (!dryRun)
        {
            File.WriteAllText(Path.Combine(root, file), text);
        }
    }

    private static IReadOnlyList<VersionRules> Load()
    {
        using var stream = typeof(UpgradeRules).Assembly.GetManifestResourceStream("upgrade-rules.json")!;
        return JsonDocument.Parse(stream).RootElement.GetProperty("versions").Deserialize<VersionRules[]>(Json)!;
    }
}
