using System.ComponentModel;
using System.Diagnostics;

namespace Coworkee.Cli;

internal static class Shell
{
    /// <summary>Runs a command with the console's output; the environment is never printed (it may hold passwords).</summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, string? workingDirectory = null, CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? environment = null,
        Action<string>? onLine = null)
    {
        Console.WriteLine($"> {string.Join(' ', args)}");
        var start = Start(args, workingDirectory);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        // with onLine the output still reaches the console, line by line, and the caller reads it too
        start.RedirectStandardOutput = start.RedirectStandardError = onLine is not null;
        using var process = Process.Start(start)!;
        if (onLine is not null)
        {
            void Echo(object _, DataReceivedEventArgs e)
            {
                if (e.Data is { } line)
                {
                    Console.WriteLine(line);
                    onLine(line);
                }
            }

            process.OutputDataReceived += Echo;
            process.ErrorDataReceived += Echo;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    /// <summary>Runs a command with its standard input from a file and/or its standard output into a file (binary, e.g. pg_dump).</summary>
    public static async Task<int> PipeAsync(IReadOnlyList<string> args, string? input, string? output, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"> {string.Join(' ', args)}{(input is null ? string.Empty : " < " + input)}{(output is null ? string.Empty : " > " + output)}");
        var start = Start(args, null);
        start.RedirectStandardInput = input is not null;
        start.RedirectStandardOutput = output is not null;
        using var process = Process.Start(start)!;
        var reading = output is null ? Task.CompletedTask : CopyAsync(process.StandardOutput.BaseStream, output, cancellationToken);
        if (input is not null)
        {
            await using (var file = File.OpenRead(input))
            {
                await file.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
            }

            process.StandardInput.Close();
        }

        await reading;
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

    private static async Task CopyAsync(Stream source, string path, CancellationToken cancellationToken)
    {
        await using var file = File.Create(path);
        await source.CopyToAsync(file, cancellationToken);
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
