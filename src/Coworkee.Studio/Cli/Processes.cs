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
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return process.ExitCode;

        void Forward(string? line, bool error)
        {
            if (line is not null)
            {
                output(line, error);
            }
        }
    }

    /// <summary>Opens a folder, file or address with the program the system associates with it.</summary>
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
