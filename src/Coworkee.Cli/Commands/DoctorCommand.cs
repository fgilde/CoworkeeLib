using System.CommandLine;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Cli.Apps;

namespace Coworkee.Cli.Commands;

internal static partial class DoctorCommand
{
    private const string DefaultSdk = "10.0.100";

    public static Command Create() => new Command("doctor", "Check the tools a Coworkee application needs and whether their versions match the app.").Handle((_, _) => RunAsync());

    public static bool SdkSatisfies(string installed, string required) =>
        System.Version.TryParse(installed.Split('-')[0], out var have) && System.Version.TryParse(required.Split('-')[0], out var need) && have >= need;

    public static string RequiredSdk(string? globalJson) =>
        globalJson is null ? DefaultSdk : JsonDocument.Parse(globalJson).RootElement.GetProperty("sdk").GetProperty("version").GetString() ?? DefaultSdk;

    /// <summary>-1, 0 or 1 like CompareTo, over the first <paramref name="parts"/> numbers; prerelease suffixes and wildcards are ignored. Null when one is no version.</summary>
    public static int? Compare(string a, string b, int parts = 3)
    {
        var x = Numbers(a);
        var y = Numbers(b);
        if (x.Length == 0 || y.Length == 0)
        {
            return null;
        }

        for (var i = 0; i < parts; i++)
        {
            var c = x.ElementAtOrDefault(i).CompareTo(y.ElementAtOrDefault(i));
            if (c != 0)
            {
                return Math.Sign(c);
            }
        }

        return 0;

        static int[] Numbers(string version) =>
            [.. version.Split('+', '-')[0].Split('.').TakeWhile(p => int.TryParse(p, out _)).Select(int.Parse)];
    }

    /// <summary>The version in &lt;Project Sdk="Aspire.AppHost.Sdk/13.6.4"&gt; of the app host.</summary>
    public static string? AspireSdk(string appHostProject) => AspireSdkVersion().Match(appHostProject) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The EF Core version of the app from Directory.Packages.props (Microsoft.EntityFrameworkCore*).</summary>
    public static string? EfRuntime(string packagesProps) => EfVersion().Match(packagesProps) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The last line of a tool's --version output ("Entity Framework Core .NET Command-line Tools\n10.0.13").</summary>
    public static string ToolVersion(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()?.Split('+')[0] ?? string.Empty;

    /// <summary>The local ports of a launchSettings.json: application URLs and the dashboard endpoints of the app host.</summary>
    public static IEnumerable<int> Ports(string launchSettings) =>
        LocalPort().Matches(launchSettings).Select(m => int.Parse(m.Groups[1].Value)).Distinct();

    private static async Task<int> RunAsync()
    {
        var root = FindRoot();
        var failures = 0;
        if (root is null)
        {
            Info("No Coworkee app here (no .slnx with a Directory.Packages.props): only the tools are checked.");
        }

        var globalJson = Path.Combine(root ?? Environment.CurrentDirectory, "global.json");
        var required = RequiredSdk(File.Exists(globalJson) ? await File.ReadAllTextAsync(globalJson) : null);
        var (sdkExit, sdk) = await Shell.CaptureAsync("dotnet", "--version");
        if (sdkExit != 0)
        {
            Fail($"No installed .NET SDK matches global.json ({required}): install it from https://dot.net or loosen \"rollForward\" in {globalJson}");
        }
        else if (SdkSatisfies(sdk, required))
        {
            Ok($".NET SDK {sdk}");
        }
        else
        {
            Fail($".NET SDK {sdk}: the app needs {required} or newer (global.json): install it from https://dot.net");
        }

        var docker = await Shell.CaptureAsync("docker", "info", "--format", "{{.ServerVersion}}");
        if (docker.ExitCode == 0)
        {
            Ok($"Docker {docker.Output}");
        }
        else
        {
            Fail("Docker is not running: start Docker Desktop (or the docker service); Aspire starts the database, Redis and Mailpit in containers");
        }

        var efRuntime = root is null ? null : EfRuntime(await File.ReadAllTextAsync(Path.Combine(root, "Directory.Packages.props")));
        var ef = await Shell.CaptureAsync("dotnet", "ef", "--version");
        var efTools = ToolVersion(ef.Output);
        if (ef.ExitCode != 0)
        {
            Fail($"dotnet-ef missing: dotnet tool install -g dotnet-ef{(efRuntime is null ? string.Empty : " --version " + efRuntime)}");
        }
        else if (efRuntime is null || Compare(efTools, efRuntime) >= 0)
        {
            Ok($"dotnet-ef {efTools}");
        }
        else if (Compare(efTools, efRuntime, 1) != 0)
        {
            Fail($"dotnet-ef {efTools} does not fit EF Core {efRuntime} of the app: dotnet tool update -g dotnet-ef --version {efRuntime}");
        }
        else
        {
            Warn($"dotnet-ef {efTools} is older than EF Core {efRuntime} of the app: dotnet tool update -g dotnet-ef --version {efRuntime}");
        }

        var appHost = root is null ? null : Directory.EnumerateFiles(Path.Combine(root, "src"), "*.AppHost.csproj", SearchOption.AllDirectories).FirstOrDefault();
        var aspireSdk = appHost is null ? null : AspireSdk(await File.ReadAllTextAsync(appHost));
        var aspire = await Shell.CaptureAsync("aspire", "--version");
        var aspireCli = ToolVersion(aspire.Output);
        if (aspire.ExitCode != 0)
        {
            Info("Aspire CLI not installed (optional, coworkee run uses dotnet run)");
        }
        else if (aspireSdk is not null && Compare(aspireCli, aspireSdk, 2) < 0)
        {
            Fail($"Aspire CLI {aspireCli} is older than the Aspire SDK {aspireSdk} of the app host, aspire run can hang: aspire update --self "
                + "(or install it again: irm https://aspire.dev/install.ps1 | iex, curl -sSL https://aspire.dev/install.sh | bash), or use coworkee run");
        }
        else
        {
            Ok($"Aspire CLI {aspireCli}");
        }

        if (root is not null && OptionApplier.Version(root) is { } appVersion)
        {
            var cli = CliApp.Version;
            switch (Compare(cli, appVersion))
            {
                case < 0:
                    Fail($"coworkee {cli} is older than the app's CoworkeeVersion {appVersion}: coworkee self-update --version {appVersion}");
                    break;
                case > 0:
                    Info($"coworkee {cli} is newer than the app's CoworkeeVersion {appVersion}: coworkee update moves the app to it");
                    break;
                default:
                    Ok($"coworkee {cli} fits the app ({appVersion})");
                    break;
            }
        }

        if (root is not null)
        {
            CheckPorts(root);
        }

        return failures == 0 ? 0 : 1;

        void Fail(string text)
        {
            Console.WriteLine($"fail  {text}");
            failures++;
        }
    }

    private static void CheckPorts(string root)
    {
        var listening = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
        var busy = new List<string>();
        foreach (var settings in Directory.EnumerateFiles(Path.Combine(root, "src"), "launchSettings.json", SearchOption.AllDirectories))
        {
            var project = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(settings)))!;
            busy.AddRange(Ports(File.ReadAllText(settings)).Where(listening.Contains).Select(port => $"{port} ({project})"));
        }

        if (busy.Count == 0)
        {
            Ok("Ports of the launch profiles are free");
            return;
        }

        var find = OperatingSystem.IsWindows() ? "netstat -ano | findstr :<port>" : "lsof -i :<port>";
        Warn($"Ports in use: {string.Join(", ", busy)}. The app or another process still runs: stop it (find it with {find}) or change the port in launchSettings.json");
    }

    private static string? FindRoot()
    {
        try
        {
            var root = Solution.Find(Environment.CurrentDirectory).Root;
            return File.Exists(Path.Combine(root, "Directory.Packages.props")) && Directory.Exists(Path.Combine(root, "src")) ? root : null;
        }
        catch (CliException)
        {
            return null;
        }
    }

    private static void Ok(string text) => Console.WriteLine($"ok    {text}");

    private static void Warn(string text) => Console.WriteLine($"warn  {text}");

    private static void Info(string text) => Console.WriteLine($"info  {text}");

    [GeneratedRegex(@"Aspire\.AppHost\.Sdk/([0-9][^""\s]*)")]
    private static partial Regex AspireSdkVersion();

    [GeneratedRegex(@"Include=""Microsoft\.EntityFrameworkCore[\w.]*""\s+Version=""([0-9][^""]*)""")]
    private static partial Regex EfVersion();

    [GeneratedRegex(@"https?://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\*|\+|\[::\]):(\d+)")]
    private static partial Regex LocalPort();
}
