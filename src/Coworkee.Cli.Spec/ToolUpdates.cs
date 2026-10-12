using System.Diagnostics;
using System.Net.Http.Json;

namespace Coworkee.Cli.Spec;

/// <summary>Newer versions of the global tools Coworkee.Cli and Coworkee.Studio on nuget.org, and the commands that update them.</summary>
public static class ToolUpdates
{
    public const string Cli = "Coworkee.Cli";
    public const string Studio = "Coworkee.Studio";
    public const string StudioCommand = "coworkee-ui";

    public static string Index(string package) => $"https://api.nuget.org/v3-flatcontainer/{package.ToLowerInvariant()}/index.json";

    /// <summary>The versions of a package on nuget.org (the flat container index lists them oldest first).</summary>
    public static async Task<string[]> FetchVersionsAsync(string package, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        return (await http.GetFromJsonAsync<VersionIndex>(Index(package), cancellationToken))?.Versions ?? [];
    }

    /// <summary>The newest version above <paramref name="current"/>; prereleases only when asked for or when the current version is one itself.</summary>
    public static string? Newer(IEnumerable<string> versions, string current, bool prerelease = false)
    {
        prerelease |= current.Contains('-');
        return versions.Where(v => prerelease || !v.Contains('-')).Where(v => Compare(v, current) > 0).OrderBy(v => v, Comparer<string>.Create(Compare)).LastOrDefault();
    }

    public static string[] UpdateArgs(string package, string version) => ["dotnet", "tool", "update", "-g", package, "--version", version];

    /// <summary>
    /// A detached process that waits until <paramref name="processId"/> has ended (Windows locks the files of a running tool), runs the
    /// commands and starts <paramref name="relaunch"/> afterwards. On Windows it opens a console window, so the user sees the update.
    /// </summary>
    public static ProcessStartInfo Helper(int processId, IReadOnlyList<string[]> commands, string[]? relaunch)
    {
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            var steps = string.Join("; ", commands.Select(c => $"& {string.Join(' ', c.Select(PowerShell))}; if ($LASTEXITCODE) {{ $failed = $true }}"));
            var launch = relaunch is null ? string.Empty
                : $"Start-Process {PowerShell(relaunch[0])}{(relaunch.Length > 1 ? " -ArgumentList " + string.Join(',', relaunch.Skip(1).Select(PowerShell)) : string.Empty)}; ";
            var script = $"Wait-Process -Id {processId} -ErrorAction SilentlyContinue; $failed = $false; {steps}; "
                + $"if ($failed) {{ Read-Host 'The update failed. Press Enter to close' }}; {launch}";
            start = new ProcessStartInfo("powershell") { ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script }, UseShellExecute = true };
        }
        else
        {
            var steps = string.Join(" ; ", commands.Select(c => string.Join(' ', c.Select(Sh))));
            var launch = relaunch is null ? string.Empty : $" ; {string.Join(' ', relaunch.Select(Sh))} >/dev/null 2>&1 &";
            start = new ProcessStartInfo("sh") { ArgumentList = { "-c", $"while kill -0 {processId} 2>/dev/null; do sleep 1; done ; {steps}{launch}" }, UseShellExecute = false };
        }

        return start;
    }

    /// <summary>The global tools folder: a freshly updated tool is there even when it is not on the PATH of this process.</summary>
    public static string ToolPath(string command) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", command + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    /// <summary>Semantic version order: numbers first, a release after its prereleases, prerelease labels part by part.</summary>
    public static int Compare(string left, string right)
    {
        var (leftCore, leftLabel) = Split(left);
        var (rightCore, rightLabel) = Split(right);
        var core = leftCore.CompareTo(rightCore);
        if (core != 0 || leftLabel == rightLabel)
        {
            return core;
        }

        if (leftLabel is null || rightLabel is null)
        {
            return leftLabel is null ? 1 : -1;
        }

        foreach (var (a, b) in leftLabel.Split('.').Zip(rightLabel.Split('.')))
        {
            var part = long.TryParse(a, out var x) && long.TryParse(b, out var y) ? x.CompareTo(y) : string.CompareOrdinal(a, b);
            if (part != 0)
            {
                return part;
            }
        }

        return leftLabel.Split('.').Length.CompareTo(rightLabel.Split('.').Length);
    }

    private static (Version Core, string? Label) Split(string version)
    {
        var parts = version.Split('+')[0].Split('-', 2);
        return (Version.TryParse(parts[0], out var core) ? core : new Version(0, 0), parts.Length > 1 ? parts[1] : null);
    }

    private static string PowerShell(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string Sh(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private sealed record VersionIndex(string[] Versions);
}
