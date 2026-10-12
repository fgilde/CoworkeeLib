using System.ComponentModel;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.Cli;

public enum GenerationOutcome
{
    Succeeded,
    Failed,
    Invalid,
    CliNotFound,
    Canceled,
}

/// <summary>Creates the app through the CLI: writes the spec, coworkee spec validate, then coworkee new --spec.</summary>
public sealed class Generator(ProcessRunner run)
{
    /// <summary>A folder per run, so two Studios (or tests) never write the same file.</summary>
    public static string SpecFile(AppSpec spec) => Path.Combine(Path.GetTempPath(), "coworkee-studio", Guid.NewGuid().ToString("n")[..12], $"{spec.Name}.json");

    public async Task<GenerationOutcome> RunAsync(AppSpec spec, StudioSettings settings, Action<CliLine> onLine, CancellationToken cancellationToken)
    {
        // the spec holds no paths, they go on the command line, so the file can live anywhere
        var file = SpecFile(spec);
        var (fileSpec, newArgs) = CommandLine.New(spec, file, settings.TemplateSource);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, SpecJson.Write(fileSpec), cancellationToken);
        var cli = CliInvocation.Resolve(settings.CliPath);
        try
        {
            if (await RunCliAsync(cli, CommandLine.Validate(file), settings, onLine, cancellationToken) != 0)
            {
                return GenerationOutcome.Invalid;
            }

            return await RunCliAsync(cli, newArgs, settings, onLine, cancellationToken) == 0
                ? GenerationOutcome.Succeeded
                : GenerationOutcome.Failed;
        }
        catch (Win32Exception)
        {
            return GenerationOutcome.CliNotFound;
        }
        catch (OperationCanceledException)
        {
            return GenerationOutcome.Canceled;
        }
    }

    public async Task<bool> InstallCliAsync(StudioSettings settings, Action<CliLine> onLine, CancellationToken cancellationToken)
    {
        var args = CommandLine.InstallCli(settings.PackageFeed);
        onLine(new CliLine(CliLineKind.Command, "dotnet " + CommandLine.Format(args)));
        try
        {
            return await run(new CliInvocation("dotnet", []).Start(args), (line, error) => onLine(CliLine.Parse(line, error)), cancellationToken) == 0;
        }
        catch (Win32Exception exception)
        {
            onLine(new CliLine(CliLineKind.Error, exception.Message));
            return false;
        }
    }

    /// <summary>
    /// Writes a secret into the user secrets of the created app host (dotnet user-secrets set); the value shows in no log line.
    /// </summary>
    public async Task<bool> StoreSecretAsync(string appHostProject, string name, string value, Action<CliLine> onLine, CancellationToken cancellationToken)
    {
        const string Mask = "********";
        onLine(new CliLine(CliLineKind.Command, CommandLine.Format(["dotnet", "user-secrets", "set", name, Mask, "--project", appHostProject])));
        var start = new System.Diagnostics.ProcessStartInfo("dotnet");
        foreach (var arg in new[] { "user-secrets", "set", name, value, "--project", appHostProject })
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            return await run(start, (line, error) => onLine(CliLine.Parse(line.Replace(value, Mask, StringComparison.Ordinal), error)), cancellationToken) == 0;
        }
        catch (Win32Exception exception)
        {
            onLine(new CliLine(CliLineKind.Error, exception.Message));
            return false;
        }
    }

    private Task<int> RunCliAsync(CliInvocation cli, string[] args, StudioSettings settings, Action<CliLine> onLine, CancellationToken cancellationToken)
    {
        var start = cli.Start(args);
        if (!string.IsNullOrWhiteSpace(settings.PackageFeed))
        {
            // MSBuild reads environment variables as properties: every restore of the new app also uses the feed
            start.Environment["RestoreAdditionalProjectSources"] = settings.PackageFeed;
        }

        onLine(new CliLine(CliLineKind.Command, CommandLine.Format([cli.FileName, .. cli.Prefix, .. args])));
        return run(start, (line, error) => onLine(CliLine.Parse(line, error)), cancellationToken);
    }
}
