using System.CommandLine;
using Coworkee.Cli.Apps;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>coworkee add and coworkee remove: options of an existing app, and with --spec also new modules and entities.</summary>
internal static class AddCommand
{
    private static readonly Argument<string[]> Changes = new("options")
    {
        Description = "Option ids, each optionally followed by its value: reports, eventBus rabbitMq, languages fr,it, ai openAI (coworkee options lists them).",
        Arity = ArgumentArity.ZeroOrMore,
    };

    private static readonly Option<FileInfo?> SpecFile = new("--spec") { Description = "Bring the app to this spec: its options, and its modules and entities the app does not have yet." };
    private static readonly Option<bool> DryRun = new("--dry-run") { Description = "Print the plan and change nothing." };
    private static readonly Option<string?> TemplateSource = new("--template-source") { Description = "The Coworkee.Templates .nupkg or folder of the app's version (default: the app's local feed, then nuget.org)." };
    private static readonly Option<bool> NoMigration = new("--no-migration") { Description = "Do not add a migration afterwards." };

    public static Command CreateAdd() => new Command("add", "Turn on options of an existing app (modules, infrastructure, languages, ...): the template's changes are merged into your files, conflicts go to .rej files, then a migration is added.")
    {
        Changes, SpecFile, DryRun, TemplateSource, NoMigration,
    }.Handle((result, cancellationToken) => RunAsync(result, remove: false, cancellationToken));

    public static Command CreateRemove() => new Command("remove", "Turn options of an existing app off; only when every file it touches is still as the template wrote it or merges cleanly.")
    {
        Changes, DryRun, TemplateSource, NoMigration,
    }.Handle((result, cancellationToken) => RunAsync(result, remove: true, cancellationToken));

    private static async Task<int> RunAsync(ParseResult result, bool remove, CancellationToken cancellationToken)
    {
        var solution = Solution.Find(Environment.CurrentDirectory);
        var (current, inferred) = AppState.Read(solution);
        if (inferred)
        {
            Console.WriteLine("No .coworkee/app.json: the options were read from the app's files.");
        }

        var words = result.GetValue(Changes) ?? [];
        var file = result.GetValue(SpecFile);
        var target = file is null ? OptionChanges.Apply(current, words, remove) : Target(solution, NewCommand.ReadSpec(file.FullName));
        var migration = file is null ? OptionChanges.MigrationName(words, remove) : "AppUpdate";
        return await RunAsync(solution, current, target, migration, new ApplyOptions(result.GetValue(DryRun), remove, result.GetValue(TemplateSource), Migration: !result.GetValue(NoMigration)), cancellationToken);
    }

    public static async Task<int> RunAsync(Solution solution, AppSpec current, AppSpec target, string migration, ApplyOptions options, CancellationToken cancellationToken)
    {
        if (SpecValidator.Validate(target) is { Count: > 0 } errors)
        {
            throw new CliException(string.Join(Environment.NewLine, errors));
        }

        foreach (var warning in SpecValidator.Warnings(target))
        {
            Console.WriteLine($"warn  {warning}");
        }

        var additions = Additions(solution, target);
        var before = SpecOptions.Resolve(current);
        var changed = SpecOptions.Resolve(target).Where(v => before[v.Key] != v.Value).Select(v => $"{v.Key}: {before[v.Key] ?? "-"} -> {v.Value ?? "-"}").ToList();
        if (changed.Count == 0 && additions.Count == 0)
        {
            Console.WriteLine("Nothing to change: the app has these options already.");
            return 0;
        }

        foreach (var line in changed)
        {
            Console.WriteLine($"option   {line}");
        }

        foreach (var (module, entities) in additions)
        {
            Console.WriteLine($"generate {module.Name}: {string.Join(", ", entities.Select(e => e.Name))}");
        }

        ApplyResult? applied = null;
        if (changed.Count > 0)
        {
            NewCommand.Progress("Merging the template's changes");
            applied = await OptionApplier.RunAsync(solution, current, target, migration, options with { Migration = options.Migration && additions.Count == 0 }, cancellationToken);
            foreach (var step in applied.Plan)
            {
                Console.WriteLine(step);
            }
        }

        if (options.DryRun)
        {
            Console.WriteLine("Dry run: nothing was changed.");
            return 0;
        }

        if (applied is { Applied: false })
        {
            Console.WriteLine("Skipped: the files above conflict with your changes, so nothing was removed. Take the option out by hand.");
            return 1;
        }

        await GenerateAsync(solution, target, additions, migration, options, cancellationToken);
        foreach (var reject in applied?.Rejects ?? [])
        {
            Console.WriteLine($"conflict {reject}");
        }

        Console.WriteLine(applied?.Rejects.Count > 0
            ? "Done with conflicts: merge each .rej into the file next to it by hand, then delete it."
            : "Done.");
        return 0;
    }

    /// <summary>A spec of Studio or the person, named like the app; modules the app has keep theirs.</summary>
    private static AppSpec Target(Solution solution, AppSpec spec)
    {
        spec.Name = solution.Name;
        spec.Output = null;
        return spec;
    }

    /// <summary>Modules of the spec the app does not have (all their entities), and new entities of modules it has.</summary>
    public static IReadOnlyList<(ModuleSpec Module, IReadOnlyList<EntitySpec> Entities)> Additions(Solution solution, AppSpec target)
    {
        var known = ExistingModel.Entities(solution);
        var additions = new List<(ModuleSpec, IReadOnlyList<EntitySpec>)>();
        foreach (var module in target.Modules)
        {
            var entities = module.Entities.Where(e => !known.Any(k => k.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            if (entities.Count > 0)
            {
                additions.Add((module, entities));
            }
        }

        return additions;
    }

    private static async Task GenerateAsync(Solution solution, AppSpec target, IReadOnlyList<(ModuleSpec Module, IReadOnlyList<EntitySpec> Entities)> additions, string migration, ApplyOptions options, CancellationToken cancellationToken)
    {
        if (additions.Count == 0)
        {
            return;
        }

        var existing = ExistingModel.Enums(solution);
        IReadOnlyList<EnumSpec> enums = [.. target.Enums.Where(e => !existing.Contains(e.Name))];
        foreach (var (module, entities) in additions)
        {
            if (Directory.Exists(Path.Combine(solution.Src, $"{solution.Name}.{module.Name}")))
            {
                foreach (var entity in entities)
                {
                    await EntityCommand.AddAsync(solution, module.Name, entity, cancellationToken, enums);
                    enums = [];
                }
            }
            else
            {
                await ModuleCommand.AddAsync(solution, new ModuleSpec { Name = module.Name, Entities = [.. entities] }, cancellationToken, enums);
                enums = [];
            }
        }

        AppState.Write(solution.Root, target);
        if (options.Migration)
        {
            await OptionApplier.MigrateAsync(solution, migration, cancellationToken);
        }
    }
}
