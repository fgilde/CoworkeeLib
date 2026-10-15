using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Coworkee.Cli.Apps;

internal enum MergeAction
{
    /// <summary>A new file of the template.</summary>
    Add,

    /// <summary>The app's file was the template's: it becomes the new one.</summary>
    Update,

    /// <summary>The app changed the file and the template's change merged in cleanly.</summary>
    Merge,

    /// <summary>The template no longer has the file and the app left it as it was.</summary>
    Delete,

    /// <summary>The app's file stays; <see cref="MergeStep.Content"/> goes next to it as <c>.rej</c>.</summary>
    Conflict,
}

/// <summary>One file of the plan, by its path relative to the solution with forward slashes.</summary>
internal sealed record MergeStep(string Path, MergeAction Action, string Note, byte[]? Content = null)
{
    public const string RejectSuffix = ".rej";

    public override string ToString() => $"{Action.ToString().ToLowerInvariant(),-8} {Path}{(Note.Length > 0 ? "  (" + Note + ")" : string.Empty)}";
}

/// <summary>
/// A 3-way merge of two outputs of the template (before: the app's options, after: the new ones) onto the app:
/// what the template changed is applied where the app left the file alone or the change merges cleanly (git merge-file);
/// everything else is a conflict that leaves the app's file untouched.
/// </summary>
internal static class TemplateMerge
{
    public static IReadOnlyList<MergeStep> Plan(string before, string after, string app, Func<string, bool>? skip = null)
    {
        var paths = Files(before).Union(Files(after)).Where(p => skip?.Invoke(p) != true).Order(StringComparer.Ordinal);
        var steps = new List<MergeStep>();
        foreach (var path in paths)
        {
            var b = Load(before, path);
            var n = Load(after, path);
            if (Same(b, n))
            {
                continue;
            }

            var u = Load(app, path);
            if (Same(u, n))
            {
                continue;
            }

            steps.Add((b, n, u) switch
            {
                (null, _, null) => new(path, MergeAction.Add, string.Empty, n),
                (null, _, _) => new(path, MergeAction.Conflict, "the app has its own file here; the template's is in the .rej", n),
                (_, null, null) => null!,
                (_, null, _) when Same(u, b) => new(path, MergeAction.Delete, string.Empty),
                (_, null, _) => new(path, MergeAction.Conflict, "the template drops this file but the app changed it; delete it when nothing uses it", []),
                (_, _, null) => new(path, MergeAction.Conflict, "the app deleted this file; the template's is in the .rej", n),
                _ when Same(u, b) => new(path, MergeAction.Update, string.Empty, LineEndingsOf(u!, n!)),
                _ => Merge(path, b!, n!, u!),
            });
        }

        return [.. steps.Where(s => s is not null)];
    }

    /// <summary>Writes the plan into the app; returns the .rej files written.</summary>
    public static IReadOnlyList<string> Apply(string app, IEnumerable<MergeStep> steps)
    {
        var rejects = new List<string>();
        foreach (var step in steps)
        {
            var file = Path.Combine(app, step.Path);
            switch (step.Action)
            {
                case MergeAction.Delete:
                    File.Delete(file);
                    break;
                case MergeAction.Conflict when step.Content is { Length: > 0 }:
                    File.WriteAllBytes(file + MergeStep.RejectSuffix, step.Content);
                    rejects.Add(step.Path + MergeStep.RejectSuffix);
                    break;
                case MergeAction.Conflict:
                    break;
                default:
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllBytes(file, step.Content!);
                    break;
            }
        }

        return rejects;
    }

    private static MergeStep Merge(string path, byte[] before, byte[] after, byte[] app)
    {
        if (IsBinary(before) || IsBinary(after) || IsBinary(app))
        {
            return new(path, MergeAction.Conflict, "binary file changed by the app; the template's is in the .rej", after);
        }

        var (conflicts, merged) = GitMerge(Normalize(app), Normalize(before), Normalize(after));
        var content = LineEndingsOf(app, merged);
        return conflicts switch
        {
            0 => new(path, MergeAction.Merge, string.Empty, content),
            > 0 => new(path, MergeAction.Conflict, $"{conflicts} conflicting change(s); the .rej holds the merge with conflict markers", content),
            _ => new(path, MergeAction.Conflict, "git was not found to merge; the template's file is in the .rej", after),
        };
    }

    /// <summary>git merge-file over the three texts: the number of conflicts (-1 without git) and the merged text.</summary>
    private static (int Conflicts, byte[] Merged) GitMerge(byte[] app, byte[] before, byte[] after)
    {
        var folder = Directory.CreateTempSubdirectory("coworkee-merge-").FullName;
        try
        {
            string Write(string name, byte[] content)
            {
                var file = Path.Combine(folder, name);
                File.WriteAllBytes(file, content);
                return file;
            }

            var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "merge-file", "-p", "-L", "app", "-L", "template before", "-L", "template after", Write("app", app), Write("before", before), Write("after", after) })
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start)!;
            using var output = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(output);
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode is >= 0 and < 128 ? (process.ExitCode, output.ToArray()) : (-1, []);
        }
        catch (Win32Exception)
        {
            return (-1, []);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static IEnumerable<string> Files(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
        : [];

    private static byte[]? Load(string root, string path) => Path.Combine(root, path) is var file && File.Exists(file) ? File.ReadAllBytes(file) : null;

    private static bool Same(byte[]? a, byte[]? b) => a is null || b is null ? a == b : Normalize(a).AsSpan().SequenceEqual(Normalize(b));

    private static bool IsBinary(byte[] content) => content.AsSpan(0, Math.Min(content.Length, 8000)).Contains((byte)0);

    private static byte[] Normalize(byte[] content) => IsBinary(content) ? content : Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(content).Replace("\r\n", "\n"));

    /// <summary>The text with the line endings the app's file uses.</summary>
    private static byte[] LineEndingsOf(byte[] app, byte[] text)
    {
        if (IsBinary(text))
        {
            return text;
        }

        var normalized = Encoding.UTF8.GetString(Normalize(text));
        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(app).Contains("\r\n", StringComparison.Ordinal) ? normalized.Replace("\n", "\r\n") : normalized);
    }
}
