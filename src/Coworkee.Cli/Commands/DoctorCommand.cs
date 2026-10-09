using System.CommandLine;
using System.Text.Json;

namespace Coworkee.Cli.Commands;

internal static class DoctorCommand
{
    private const string DefaultSdk = "10.0.100";

    public static Command Create() => new Command("doctor", "Check the tools a Coworkee application needs.").Handle((_, _) => RunAsync());

    public static bool SdkSatisfies(string installed, string required) =>
        System.Version.TryParse(installed.Split('-')[0], out var have) && System.Version.TryParse(required.Split('-')[0], out var need) && have >= need;

    public static string RequiredSdk(string? globalJson) =>
        globalJson is null ? DefaultSdk : JsonDocument.Parse(globalJson).RootElement.GetProperty("sdk").GetProperty("version").GetString() ?? DefaultSdk;

    private static async Task<int> RunAsync()
    {
        var globalJson = Path.Combine(Environment.CurrentDirectory, "global.json");
        var required = RequiredSdk(File.Exists(globalJson) ? await File.ReadAllTextAsync(globalJson) : null);
        var (_, sdk) = await Shell.CaptureAsync("dotnet", "--version");
        var docker = await Shell.CaptureAsync("docker", "info", "--format", "{{.ServerVersion}}");
        var ef = await Shell.CaptureAsync("dotnet", "ef", "--version");
        var aspire = await Shell.CaptureAsync("aspire", "--version");

        var failures = 0;
        failures += Report(SdkSatisfies(sdk, required), $".NET SDK {sdk}", $".NET SDK {sdk}: needs {required} or newer");
        failures += Report(docker.ExitCode == 0, $"Docker {docker.Output}", "Docker not running: Aspire starts Postgres, Redis and Mailpit in containers");
        failures += Report(ef.ExitCode == 0, "dotnet-ef", "dotnet-ef missing: dotnet tool install -g dotnet-ef");
        Console.WriteLine(aspire.ExitCode == 0 ? $"info  Aspire CLI {aspire.Output.Split('+')[0]}" : "info  Aspire CLI not installed (optional, coworkee run uses dotnet run)");
        return failures == 0 ? 0 : 1;
    }

    private static int Report(bool ok, string success, string failure)
    {
        Console.WriteLine(ok ? $"ok    {success}" : $"fail  {failure}");
        return ok ? 0 : 1;
    }
}
