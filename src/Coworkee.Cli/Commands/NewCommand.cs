using System.CommandLine;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

internal static class NewCommand
{
    private const int AlreadyInstalled = 106;

    private static readonly Argument<string?> Name = Names.Optional("name", "Name of the app, used for projects and namespaces (default: the name in the spec).");
    private static readonly Option<string?> Output = new("--output", "-o") { Description = "Target folder (default: ./<name>, next to the spec with --spec)." };
    private static readonly Option<FileInfo?> SpecFile = new("--spec") { Description = "Create the app from a JSON spec (coworkee spec example); switches given here win over the spec." };
    private static readonly Option<bool> NoSamples = new("--no-samples") { Description = "Same as --samples false." };
    private static readonly Option<bool> NoTests = new("--no-tests") { Description = "Same as --tests false." };
    private static readonly Option<string?> TemplateSource = new("--template-source") { Description = "Install the template from this .nupkg or folder instead of nuget.org." };

    private static readonly IReadOnlyList<(CliOption Option, Option Symbol)> Switches = [.. OptionCatalog.All.Select(o => (o, Switch(o)))];

    public static Command Create()
    {
        var command = new Command("new", "Create a Coworkee application; coworkee options describes every switch.") { Name, Output, SpecFile, NoSamples, NoTests, TemplateSource };
        foreach (var (_, symbol) in Switches)
        {
            command.Options.Add(symbol);
        }

        return command.Handle(RunAsync);
    }

    public static NewSettings Read(ParseResult result)
    {
        var file = result.GetValue(SpecFile);
        var spec = file is null ? new AppSpec() : ReadSpec(file.FullName);
        var baseDirectory = file?.DirectoryName ?? Environment.CurrentDirectory;
        spec.Name = result.GetValue(Name) ?? spec.Name;
        foreach (var (option, symbol) in Switches)
        {
            if (result.GetResult(symbol) is { Implicit: false })
            {
                spec.Options[option.Id] = option.Kind == OptionKind.Bool ? result.GetValue((Option<bool?>)symbol) ?? true : result.GetValue((Option<string?>)symbol);
            }
        }

        if (result.GetValue(NoSamples))
        {
            spec.Options["samples"] = false;
        }

        if (result.GetValue(NoTests))
        {
            spec.Options["tests"] = false;
        }

        var errors = SpecValidator.Validate(spec, baseDirectory);
        if (errors.Count > 0)
        {
            throw new CliException(string.Join(Environment.NewLine, errors));
        }

        var output = result.GetValue(Output) ?? Path.Combine(baseDirectory, spec.Output ?? spec.Name);
        return new NewSettings(spec, baseDirectory, Path.GetFullPath(output), result.GetValue(TemplateSource));
    }

    public static AppSpec ReadSpec(string path)
    {
        try
        {
            return SpecJson.Read(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            throw new CliException($"Cannot read the spec {path}: {exception.Message}");
        }
    }

    public static string[] InstallArgs(NewSettings settings, string version) => settings.TemplateSource is { } source
        ? ["dotnet", "new", "install", Path.GetFullPath(source), "--force"]
        : ["dotnet", "new", "install", $"Coworkee.Templates@{version}"];

    /// <summary>dotnet new coworkee with every template option that differs from its default; files become the names they get in the app.</summary>
    public static string[] TemplateArgs(NewSettings settings)
    {
        var values = SpecOptions.Resolve(settings.Spec);
        var args = new List<string> { "dotnet", "new", "coworkee", "-n", settings.Spec.Name, "-o", settings.Output };
        foreach (var option in OptionCatalog.All.Where(o => o.TemplateSymbol))
        {
            if (values[option.Id] is { } value && value != option.Default)
            {
                args.AddRange([$"--{option.Id}", Assets.TemplateValue(option.Id, value)]);
            }
        }

        return [.. args];
    }

    /// <summary>After dotnet new: files, the modules of the spec, restore, the migration they need and git.</summary>
    public static async Task<int> CompleteAsync(NewSettings settings, CancellationToken cancellationToken)
    {
        var values = SpecOptions.Resolve(settings.Spec);
        var solution = Solution.Find(settings.Output);
        Assets.Copy(settings, values);
        foreach (var module in settings.Spec.Modules)
        {
            Progress($"Generating module {module.Name}");
            var entities = module.Entities.Select(e => new EntityModel(solution.Name, module.Name, e, SpecOptions.Mapping(values, e))).ToList();
            await FeatureWriter.AddModuleAsync(solution, module.Name, entities, cancellationToken);
        }

        Progress("Restoring packages");
        var exit = await Shell.RunAsync(["dotnet", "restore"], settings.Output, cancellationToken);
        if (exit != 0)
        {
            return exit;
        }

        if (MigrationName(solution, settings.Spec) is { } migration)
        {
            Progress($"Adding the migration {migration}");
            if (await Shell.RunAsync(MigrationsCommand.AddArgs(solution, migration), settings.Output, cancellationToken) != 0)
            {
                Console.WriteLine($"warn  The migration could not be added. Install dotnet-ef (coworkee doctor), then run coworkee migrations add {migration} in {settings.Output}.");
            }
        }

        if (values["git"] == "true")
        {
            Progress("Initializing git");
            await Shell.RunAsync(["git", "init", "--quiet"], settings.Output, cancellationToken);
        }

        return 0;
    }

    /// <summary>Initial when the template left out its migration (other modules than the shipped one), Add... for generated modules, else none.</summary>
    public static string? MigrationName(Solution solution, AppSpec spec) =>
        !Directory.Exists(Path.Combine(Path.GetDirectoryName(solution.Project("Infrastructure"))!, "Migrations")) ? "Initial"
        : spec.Modules.Count > 0 ? "Add" + string.Concat(spec.Modules.Select(m => m.Name))
        : null;

    public static void Progress(string step) => Console.WriteLine($"==> {step}");

    private static Option Switch(CliOption option)
    {
        var description = option.Default is null ? option.Description : $"{option.Description} (default: {option.Default})";
        if (option.Kind == OptionKind.Bool)
        {
            return new Option<bool?>(option.Flag) { Description = description };
        }

        var text = new Option<string?>(option.Flag) { Description = description, HelpName = option.Kind switch { OptionKind.Color => "#rrggbb", OptionKind.File => "file", _ => null } };
        if (option.Choices is { } choices)
        {
            text.AcceptOnlyFromAmong([.. choices.Select(c => c.Value)]);
        }

        return text;
    }

    private static async Task<int> RunAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var settings = Read(result);
        Progress("Installing the template");
        var installed = await Shell.RunAsync(InstallArgs(settings, CliApp.Version), cancellationToken: cancellationToken);
        if (installed != 0 && installed != AlreadyInstalled)
        {
            return installed;
        }

        Progress("Creating the solution");
        var exit = await Shell.RunAsync(TemplateArgs(settings), cancellationToken: cancellationToken);
        if (exit == 0)
        {
            exit = await CompleteAsync(settings, cancellationToken);
        }

        if (exit != 0)
        {
            return exit;
        }

        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  cd {settings.Output}");
        Console.WriteLine("  coworkee run");
        Console.WriteLine($"Sign in as admin@{settings.Spec.Name.ToLowerInvariant()}.local, the password is in src/{settings.Spec.Name}.Migrations/DemoSeed.cs.");
        return 0;
    }
}

internal sealed record NewSettings(AppSpec Spec, string BaseDirectory, string Output, string? TemplateSource);
