using System.Text.RegularExpressions;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Apps;

/// <summary>How <c>coworkee add</c>/<c>remove</c> runs: the template from <see cref="TemplateSource"/> or nuget.org, or one already installed in <see cref="Hive"/>.</summary>
internal sealed record ApplyOptions(bool DryRun, bool RequireClean, string? TemplateSource = null, string? Hive = null, bool Migration = true);

/// <summary>The result: the plan, the .rej files written and whether it was applied.</summary>
internal sealed record ApplyResult(IReadOnlyList<MergeStep> Plan, IReadOnlyList<string> Rejects, bool Applied);

/// <summary>
/// Turns options of an existing app on or off: the template is generated with the app's options and with the new ones,
/// the difference is merged into the app (<see cref="TemplateMerge"/>), then restore and a migration when the model changed.
/// </summary>
internal static partial class OptionApplier
{
    public static async Task<ApplyResult> RunAsync(Solution solution, AppSpec current, AppSpec target, string migration, ApplyOptions options, CancellationToken cancellationToken)
    {
        var work = Directory.CreateTempSubdirectory("coworkee-add-").FullName;
        try
        {
            var hive = options.Hive ?? await InstallAsync(solution, Path.Combine(work, "hive"), options.TemplateSource, cancellationToken);
            var before = await GenerateAsync(current, Path.Combine(work, "before"), hive, cancellationToken);
            var after = await GenerateAsync(target, Path.Combine(work, "after"), hive, cancellationToken);
            if (AdminPassword(solution.Root) is { } password)
            {
                UsePassword(before, password);
                UsePassword(after, password);
            }

            var migrations = $"src/{solution.Name}.Infrastructure/Migrations/";
            var plan = TemplateMerge.Plan(before, after, solution.Root, p => p.StartsWith(migrations, StringComparison.Ordinal));
            if (options.DryRun || (options.RequireClean && plan.Any(s => s.Action == MergeAction.Conflict)))
            {
                return new(plan, [], false);
            }

            var rejects = TemplateMerge.Apply(solution.Root, plan);
            AppState.Write(solution.Root, target);
            if (options.Migration)
            {
                await MigrateAsync(solution, migration, cancellationToken);
            }

            return new(plan, rejects, true);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The Coworkee version of the app, from Directory.Packages.props.</summary>
    public static string? Version(string root) =>
        Path.Combine(root, "Directory.Packages.props") is var props && File.Exists(props) && CoworkeeVersion().Match(File.ReadAllText(props)) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    /// <summary>The template of the app's version: --template-source, a Coworkee.Templates package in the app's local feed, else nuget.org.</summary>
    private static async Task<string> InstallAsync(Solution solution, string hive, string? source, CancellationToken cancellationToken)
    {
        var version = Version(solution.Root) ?? throw new CliException("Directory.Packages.props has no <CoworkeeVersion>.");
        var local = LocalFeed.Folders(solution.Root).Select(f => Path.Combine(f, $"Coworkee.Templates.{version}.nupkg")).FirstOrDefault(File.Exists);
        var package = source is not null ? Path.GetFullPath(source) : local ?? $"Coworkee.Templates@{version}";
        if (await Shell.RunAsync(["dotnet", "new", "install", package, "--debug:custom-hive", hive], cancellationToken: cancellationToken) != 0)
        {
            throw new CliException($"The template {package} could not be installed. Pass --template-source with the Coworkee.Templates package of version {version}.");
        }

        return hive;
    }

    private static async Task<string> GenerateAsync(AppSpec spec, string output, string hive, CancellationToken cancellationToken)
    {
        string[] args = [.. NewCommand.TemplateArgs(new NewSettings(spec, ".", output, null)), "--debug:custom-hive", hive];
        if (await Shell.RunAsync(args, cancellationToken: cancellationToken) != 0)
        {
            throw new CliException("dotnet new coworkee failed; see above.");
        }

        return output;
    }

    public static async Task MigrateAsync(Solution solution, string migration, CancellationToken cancellationToken)
    {
        if (await Shell.RunAsync(["dotnet", "restore"], solution.Root, cancellationToken) != 0)
        {
            Console.WriteLine("warn  dotnet restore failed; fix it, then run coworkee migrations add " + migration);
            return;
        }

        var infrastructure = solution.Project("Infrastructure");
        if (await Shell.RunAsync(["dotnet", "ef", "migrations", "has-pending-model-changes", "--project", infrastructure, "--startup-project", infrastructure], solution.Root, cancellationToken) == 0)
        {
            Console.WriteLine("The database model did not change; no migration needed.");
            return;
        }

        if (await Shell.RunAsync([.. MigrationsCommand.AddArgs(solution, migration), "--no-build"], solution.Root, cancellationToken) != 0)
        {
            Console.WriteLine($"warn  The migration could not be added. Install dotnet-ef (coworkee doctor), then run coworkee migrations add {migration}.");
        }
    }

    /// <summary>The generated administrator password of the app: both template outputs get it, so DemoUsers.cs and LOGIN.md differ in nothing else.</summary>
    private static string? AdminPassword(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "DemoUsers.cs", SearchOption.AllDirectories).FirstOrDefault() is { } file
        && SeedPassword().Match(File.ReadAllText(file)) is { Success: true } match ? match.Value : null;

    private static void UsePassword(string folder, string password)
    {
        var demoUsers = Directory.EnumerateFiles(folder, "DemoUsers.cs", SearchOption.AllDirectories).FirstOrDefault();
        if (demoUsers is null || SeedPassword().Match(File.ReadAllText(demoUsers)) is not { Success: true } generated)
        {
            return;
        }

        foreach (var file in new[] { demoUsers, Path.Combine(folder, "LOGIN.md") }.Where(File.Exists))
        {
            File.WriteAllText(file, File.ReadAllText(file).Replace(generated.Value, password, StringComparison.Ordinal));
        }
    }

    [GeneratedRegex("<CoworkeeVersion>([^<]+)</CoworkeeVersion>")]
    private static partial Regex CoworkeeVersion();

    [GeneratedRegex("Cw1![0-9a-fA-F]{32}")]
    private static partial Regex SeedPassword();
}
