using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Coworkee.Testing;

/// <summary>
/// Golden files: <c>Golden.Match(actual)</c> compares text with <c>Golden/{test}.txt</c> next to the calling test file and fails on any difference.
/// GUIDs become <c>&lt;guid1&gt;</c>, <c>&lt;guid2&gt;</c>... in the order they first appear, so ids generated per run compare and the same id stays recognisable;
/// assembly versions become <c>Version=*</c>.
/// With the environment variable <see cref="UpdateVariable"/> set to 1 it writes the file instead (review the diff before committing it).
/// </summary>
public static partial class Golden
{
    public const string UpdateVariable = "COWORKEE_UPDATE_GOLDEN";

    public static void Match(string actual, [CallerMemberName] string name = "", [CallerFilePath] string file = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(file)!, "Golden", $"{name}.txt");
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        actual = Guids().Replace(actual.ReplaceLineEndings("\n"), m => ids.TryGetValue(m.Value, out var known) ? known : ids[m.Value] = $"<guid{ids.Count + 1}>");
        actual = Versions().Replace(actual, "Version=*");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        if (!File.Exists(path))
        {
            throw new GoldenMismatchException($"No golden file {path}; run the test once with {UpdateVariable}=1.");
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (expected != actual)
        {
            File.WriteAllText(Path.ChangeExtension(path, ".received.txt"), actual);
            string[] want = expected.Split('\n'), got = actual.Split('\n');
            var line = want.Zip(got).TakeWhile(p => p.First == p.Second).Count();
            throw new GoldenMismatchException(
                $"{path} differs from line {line + 1}:\n- {want.ElementAtOrDefault(line)}\n+ {got.ElementAtOrDefault(line)}\n(the whole output is in the .received.txt next to it)");
        }
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guids();

    // assembly-qualified type names carry the package version
    [GeneratedRegex(@"Version=\d+(\.\d+)*")]
    private static partial Regex Versions();
}

/// <summary>The output differs from its golden file.</summary>
public sealed class GoldenMismatchException(string message) : Exception(message);
