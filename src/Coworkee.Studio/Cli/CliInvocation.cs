using System.Diagnostics;

namespace Coworkee.Studio.Cli;

/// <summary>How the coworkee CLI is started: the tool on the PATH, an executable, or <c>dotnet Coworkee.Cli.dll</c>.</summary>
public sealed record CliInvocation(string FileName, IReadOnlyList<string> Prefix)
{
    public static string ToolsFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");

    public static CliInvocation Resolve(string? path, string? toolsFolder = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            // a tool installed a moment ago is in ~/.dotnet/tools even when that folder is not on the PATH of this process yet
            var tool = Path.Combine(toolsFolder ?? ToolsFolder, "coworkee" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            return new(File.Exists(tool) ? tool : "coworkee", []);
        }

        return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? new("dotnet", [path]) : new(path, []);
    }

    public ProcessStartInfo Start(IEnumerable<string> args, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(FileName) { WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory };
        foreach (var arg in Prefix.Concat(args))
        {
            start.ArgumentList.Add(arg);
        }

        return start;
    }
}
