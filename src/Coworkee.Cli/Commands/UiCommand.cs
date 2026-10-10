using System.CommandLine;

namespace Coworkee.Cli.Commands;

/// <summary>Starts Coworkee Studio (the desktop wizard, global tool Coworkee.Studio), installing or updating it to the version of this CLI first.</summary>
internal static class UiCommand
{
    public const string Package = "Coworkee.Studio";
    public const string ToolCommand = "coworkee-ui";

    private static readonly Option<string?> ToolVersion = new("--studio-version") { Description = "Studio version to install (default: the version of this CLI)." };
    private static readonly Option<bool> Prerelease = new("--prerelease") { Description = "Install the newest prerelease of Coworkee Studio instead of the CLI's version; ignored with --studio-version." };
    private static readonly Option<string?> Source = new("--add-source") { Description = "Additional NuGet source for the Studio package, e.g. a folder with local packs." };

    public enum Step
    {
        None,
        Install,
        Update,

        /// <summary>An exact --studio-version below the installed one.</summary>
        Downgrade,
    }

    public static Command Create()
    {
        var command = new Command("ui", $"Open Coworkee Studio, the desktop wizard that configures and creates an app with this CLI. Installs or updates the global tool {Package} to the CLI version first; other arguments (for example --spec app.json) go to {ToolCommand}.")
        {
            ToolVersion, Prerelease, Source,
        };
        command.TreatUnmatchedTokensAsErrors = false;
        return command.Handle(RunAsync);
    }

    public static string? Installed(string toolList) => toolList.Split('\n')
        .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .FirstOrDefault(columns => columns.Length >= 2 && columns[0].Equals(Package, StringComparison.OrdinalIgnoreCase))?[1];

    /// <summary>Install when missing, update when older; an <paramref name="exact"/> version (--studio-version) is also installed when the installed one is newer.</summary>
    public static Step Plan(string? installed, string wanted, bool exact = false) => installed is null ? Step.Install : Compare(installed, wanted) switch
    {
        < 0 => Step.Update,
        > 0 when exact => Step.Downgrade,
        _ => Step.None,
    };

    // dotnet tool refuses --version together with --prerelease: --prerelease means the newest prerelease instead of a fixed version
    public static string[] ToolArgs(Step step, string version, bool prerelease, string? source) =>
    [
        "dotnet", "tool", step == Step.Install ? "install" : "update", "-g", Package,
        .. prerelease ? new[] { "--prerelease" } : ["--version", version],
        .. step == Step.Downgrade ? new[] { "--allow-downgrade" } : [],
        .. source is null ? [] : new[] { "--add-source", Path.GetFullPath(source) },
    ];

    public static string[] LaunchArgs(string launcher, IEnumerable<string> passThrough) => [launcher, .. passThrough];

    // a tool installed a moment ago is in ~/.dotnet/tools even when that folder is not on the PATH of this process yet
    public static string Launcher(string toolsFolder) =>
        Path.Combine(toolsFolder, ToolCommand + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)) is var path && File.Exists(path) ? path : ToolCommand;

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

    private static async Task<int> RunAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var wanted = result.GetValue(ToolVersion) ?? CliApp.Version;
        var (_, list) = await Shell.CaptureAsync("dotnet", "tool", "list", "-g");
        var step = Plan(Installed(list), wanted, exact: result.GetValue(ToolVersion) is not null);
        if (step != Step.None)
        {
            var exit = await Shell.RunAsync(ToolArgs(step, wanted, result.GetValue(Prerelease) && result.GetValue(ToolVersion) is null, result.GetValue(Source)), cancellationToken: cancellationToken);
            if (exit != 0)
            {
                Console.Error.WriteLine($"Could not {step.ToString().ToLowerInvariant()} {Package} {wanted}. Is it published for this version? Try --prerelease or --add-source <folder>.");
                return exit;
            }
        }

        var tools = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");
        return await Shell.RunAsync(LaunchArgs(Launcher(tools), result.UnmatchedTokens), cancellationToken: cancellationToken);
    }
}
