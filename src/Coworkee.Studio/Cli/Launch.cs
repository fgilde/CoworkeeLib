using System.Diagnostics;

namespace Coworkee.Studio.Cli;

/// <summary>Editors for a created app.</summary>
public static class Launch
{
    /// <summary>A command line program like code or rider; on Windows those are .cmd files that only cmd starts.</summary>
    public static void Tool(string tool, string argument, string workingDirectory)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd") { ArgumentList = { "/c", tool, argument }, CreateNoWindow = true }
            : new ProcessStartInfo(tool) { ArgumentList = { argument } };
        start.WorkingDirectory = workingDirectory;
        start.UseShellExecute = false;
        Process.Start(start)?.Dispose();
    }
}
