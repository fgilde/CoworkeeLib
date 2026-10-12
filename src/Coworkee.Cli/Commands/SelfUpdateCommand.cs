using System.CommandLine;
using System.Diagnostics;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>Updates the global tools: this CLI and, when installed, Coworkee Studio, both to the same version.</summary>
internal static class SelfUpdateCommand
{
    private static readonly Option<string?> Version = new("--version") { Description = "Version to install (default: the newest on nuget.org)." };
    private static readonly Option<bool> Prerelease = new("--prerelease") { Description = "Allow prerelease versions when looking up the newest (always on when this CLI is a prerelease)." };

    public static Command Create() => new Command("self-update", "Update the coworkee CLI and Coworkee Studio (when installed) to the newest version. For the Coworkee packages of an app see coworkee update.")
        {
            Version, Prerelease,
        }
        .Handle(RunAsync);

    /// <summary>The update commands: the CLI always, the Studio only when it is installed.</summary>
    public static IReadOnlyList<string[]> Commands(string version, bool studioInstalled) =>
        studioInstalled
            ? [ToolUpdates.UpdateArgs(ToolUpdates.Cli, version), ToolUpdates.UpdateArgs(ToolUpdates.Studio, version)]
            : [ToolUpdates.UpdateArgs(ToolUpdates.Cli, version)];

    private static async Task<int> RunAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var version = result.GetValue(Version) ?? await NewestAsync(result.GetValue(Prerelease), cancellationToken);
        if (version is null)
        {
            Console.WriteLine($"coworkee {CliApp.Version} is the newest version.");
            return 0;
        }

        var (_, list) = await Shell.CaptureAsync("dotnet", "tool", "list", "-g");
        var commands = Commands(version, UiCommand.Installed(list) is not null);
        if (!OperatingSystem.IsWindows())
        {
            foreach (var command in commands)
            {
                if (await Shell.RunAsync(command, cancellationToken: cancellationToken) is var exit and not 0)
                {
                    return exit;
                }
            }

            return 0;
        }

        // Windows locks the files of the running coworkee.exe: the update runs in a window of its own once this process has ended
        Process.Start(ToolUpdates.Helper(Environment.ProcessId, commands, null))?.Dispose();
        Console.WriteLine($"Updating to {version} in a new window once this command has ended.");
        return 0;
    }

    private static async Task<string?> NewestAsync(bool prerelease, CancellationToken cancellationToken)
    {
        try
        {
            return ToolUpdates.Newer(await ToolUpdates.FetchVersionsAsync(ToolUpdates.Cli, cancellationToken), CliApp.Version, prerelease);
        }
        catch (HttpRequestException exception)
        {
            throw new CliException($"Could not read the versions from nuget.org ({exception.Message}). Pass --version.");
        }
    }
}
