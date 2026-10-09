using System.ComponentModel;
using System.Diagnostics;

namespace Coworkee.Cli;

internal static class Shell
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"> {string.Join(' ', args)}");
        using var process = Process.Start(Start(args, workingDirectory))!;
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public static async Task<(int ExitCode, string Output)> CaptureAsync(params string[] args)
    {
        var start = Start(args, null);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, (await output).Trim());
        }
        catch (Win32Exception)
        {
            return (-1, string.Empty);
        }
    }

    private static ProcessStartInfo Start(IReadOnlyList<string> args, string? workingDirectory)
    {
        var start = new ProcessStartInfo(args[0]) { WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory };
        foreach (var arg in args.Skip(1))
        {
            start.ArgumentList.Add(arg);
        }

        return start;
    }
}
