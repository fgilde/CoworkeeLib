using System.CommandLine;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;
using Coworkee.Cli.Spec.Import;

namespace Coworkee.Cli.Commands;

internal static class EntityImportCommand
{
    private static readonly Option<ImportSource> From = new("--from") { Description = "What to read: db (Postgres, SQL Server or SQLite), openapi (OpenAPI 3 JSON) or csv.", Required = true };
    private static readonly Argument<string> Source = new("source") { Description = "db: a connection string or SQLite file; openapi: a JSON file or http(s) address; csv: a file with a header row." };
    private static readonly Option<string?> Module = new("--module") { Description = "Module of the entities (default: the database name, the API title or the CSV file name)." };
    private static readonly Option<string?> Tables = new("--tables") { Description = "Only these tables or schemas, comma separated, e.g. orders,customers." };
    private static readonly Option<string?> Provider = new("--provider") { Description = "db: postgres, sqlServer or sqlite (default: guessed from the connection string)." };
    private static readonly Option<bool> DryRun = new("--dry-run") { Description = "Print the spec and the problems, change nothing." };
    private static readonly Option<FileInfo?> SpecOut = new("--spec-out") { Description = "Write the module into this spec file (added to an existing spec) instead of the app." };

    public static Command Create() => new Command("import", "Import entities from a database, an OpenAPI document or a CSV file into a module of the app, or into a spec.")
    {
        Source, From, Module, Tables, Provider, DryRun, SpecOut,
    }.Handle(RunAsync);

    public static async Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await EntityImporter.ImportAsync(request, cancellationToken);
        }
        catch (ImportException exception)
        {
            throw new CliException(exception.Message);
        }
    }

    public static IEnumerable<string> Report(ImportResult result) =>
    [
        $"Module {result.Module.Name}: {result.Module.Entities.Count} entities",
        .. result.Lines().Select(l => "  " + l),
        .. result.Enums.Select(e => $"  enum {e.Name}: {string.Join(", ", e.Values)}"),
        .. result.Skipped.Count == 0 ? [] : new[] { "Skipped:" }.Concat(result.Skipped.Select(s => "  " + s)),
    ];

    /// <summary>Adds the module to the spec file, replacing a module of the same name; a missing file becomes a spec named like the module.</summary>
    public static AppSpec MergeInto(string file, ImportResult result)
    {
        var spec = File.Exists(file) ? NewCommand.ReadSpec(file) : new AppSpec { Name = result.Module.Name + "App" };
        spec.Modules.RemoveAll(m => m.Name.Equals(result.Module.Name, StringComparison.OrdinalIgnoreCase));
        spec.Modules.Add(result.Module);
        spec.Enums.AddRange(result.Enums.Where(e => spec.Enums.All(x => !x.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase))));
        return spec;
    }

    /// <summary>A new module is created in one go; into an existing one the entities go one by one, referenced ones first.</summary>
    public static async Task<int> ApplyAsync(Solution solution, ImportResult result, CancellationToken cancellationToken)
    {
        var existing = ExistingModel.Enums(solution);
        var enums = result.Enums.Where(e => !existing.Contains(e.Name)).ToList();
        if (!Directory.Exists(Path.Combine(solution.Src, $"{solution.Name}.{result.Module.Name}")))
        {
            return await ModuleCommand.AddAsync(solution, result.Module, cancellationToken, enums);
        }

        var spec = new AppSpec { Name = solution.Name, Options = { ["samples"] = solution.HasSamples }, Modules = [result.Module], Enums = enums };
        FeatureOptions.Validate(spec, ExistingModel.Index(solution, spec));
        foreach (var (entity, index) in ReferencedFirst(result.Module.Entities).Select((e, i) => (e, i)))
        {
            await EntityCommand.AddAsync(solution, result.Module.Name, entity, cancellationToken, index == 0 ? enums : []);
        }

        return 0;
    }

    private static List<EntitySpec> ReferencedFirst(List<EntitySpec> entities)
    {
        var ordered = new List<EntitySpec>();
        var rest = entities.ToList();
        while (rest.Count > 0)
        {
            var next = rest.FirstOrDefault(e => e.Properties.All(p => p.Reference is null || p.Reference == e.Name || rest.All(r => r.Name != p.Reference))) ?? rest[0];
            ordered.Add(next);
            rest.Remove(next);
        }

        return ordered;
    }

    private static async Task<int> RunAsync(ParseResult parse, CancellationToken cancellationToken)
    {
        var tables = parse.GetValue(Tables)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = await ImportAsync(new ImportRequest(parse.GetValue(From), parse.GetValue(Source)!, parse.GetValue(Module), tables, parse.GetValue(Provider)), cancellationToken);
        foreach (var line in Report(result))
        {
            Console.WriteLine(line);
        }

        if (parse.GetValue(SpecOut) is { } file)
        {
            var spec = MergeInto(file.FullName, result);
            var errors = SpecValidator.Validate(spec, file.DirectoryName);
            if (parse.GetValue(DryRun))
            {
                Console.WriteLine(SpecJson.Write(spec));
            }
            else
            {
                await File.WriteAllTextAsync(file.FullName, SpecJson.Write(spec), cancellationToken);
                Console.WriteLine($"Wrote {file.FullName}.");
            }

            return Problems(errors);
        }

        if (parse.GetValue(DryRun))
        {
            var app = TryFind();
            var spec = result.ToSpec(app?.Name ?? "App");
            spec.Options["samples"] = app?.HasSamples ?? false;
            Console.WriteLine(SpecJson.Write(spec));
            return Problems(app is null ? SpecValidator.Validate(spec) : SpecValidator.Validate(spec, null, [], ExistingModel.Index(app, spec)));
        }

        var solution = Solution.Find(Environment.CurrentDirectory);
        await ApplyAsync(solution, result, cancellationToken);
        Console.WriteLine($"Imported {result.Module.Entities.Count} entities into {result.Module.Name}. Next: coworkee migrations add Import{result.Module.Name}");
        return 0;
    }

    private static int Problems(IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
        {
            Console.WriteLine("error " + error);
        }

        return errors.Count == 0 ? 0 : 1;
    }

    private static Solution? TryFind()
    {
        try
        {
            return Solution.Find(Environment.CurrentDirectory);
        }
        catch (CliException)
        {
            return null;
        }
    }
}
