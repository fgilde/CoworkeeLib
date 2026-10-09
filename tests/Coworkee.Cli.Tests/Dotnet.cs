using System.Diagnostics;

namespace Coworkee.Cli.Tests;

internal static class Dotnet
{
    public static async Task<string> RunAsync(string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var text = await output + await error;
        process.ExitCode.ShouldBe(0, $"dotnet {string.Join(' ', args)}{Environment.NewLine}{text}");
        return text;
    }

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Coworkee.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Coworkee.slnx not found above the test output.");
    }
}
