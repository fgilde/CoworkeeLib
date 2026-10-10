using System.Diagnostics;

namespace Coworkee.Studio.Cli;

/// <summary>Editors and terminals for a created app.</summary>
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

    /// <summary>A new terminal window in <paramref name="workingDirectory"/> that runs <paramref name="command"/> and stays open.</summary>
    // ponytail: Terminal.app on macOS and x-terminal-emulator on Linux; other terminals need the command typed by hand
    public static void Terminal(string command, string workingDirectory)
    {
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            start = new ProcessStartInfo("cmd") { ArgumentList = { "/c", "start", "Coworkee", "cmd", "/k", command } };
        }
        else if (OperatingSystem.IsMacOS())
        {
            start = new ProcessStartInfo("osascript") { ArgumentList = { "-e", $"tell application \"Terminal\" to do script \"cd '{workingDirectory}' && {command}\"" } };
        }
        else
        {
            start = new ProcessStartInfo("x-terminal-emulator") { ArgumentList = { "-e", "sh", "-c", $"{command}; exec sh" } };
        }

        start.WorkingDirectory = workingDirectory;
        start.UseShellExecute = false;
        Process.Start(start)?.Dispose();
    }
}
