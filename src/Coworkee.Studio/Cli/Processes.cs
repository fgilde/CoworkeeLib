using System.Diagnostics;
using System.Text;

namespace Coworkee.Studio.Cli;

/// <summary>Starts a process and hands every line of stdout (false) and stderr (true) to <paramref name="output"/>; throws Win32Exception when the program is missing.</summary>
public delegate Task<int> ProcessRunner(ProcessStartInfo start, Action<string, bool> output, CancellationToken cancellationToken);

public static class Processes
{
    public static async Task<int> RunAsync(ProcessStartInfo start, Action<string, bool> output, CancellationToken cancellationToken)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Forward(e.Data, false);
        process.ErrorDataReceived += (_, e) => Forward(e.Data, true);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // at once, so a Studio that closes takes the app host with it
        using var kill = cancellationToken.Register(() => Kill(process));
        await process.WaitForExitAsync(cancellationToken);

        return process.ExitCode;

        void Forward(string? line, bool error)
        {
            if (line is not null)
            {
                output(line, error);
            }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already ended
        }
    }

    /// <summary>Opens a folder, file or address with the program the system associates with it.</summary>
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
