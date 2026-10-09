using System.CommandLine;

namespace Coworkee.Cli.Commands;

internal static class ModuleCommand
{
    private const string Prefix = "Scaffold/";

    private static readonly Argument<string> Name = Names.Simple("name", "Name of the module, for example Orders.");
    private static readonly Option<string?> Entity = new("--entity") { Description = "Name of its first entity (default: the module name without a trailing s)." };

    public static Command Create()
    {
        var add = new Command("add", "Add a feature module with one entity, permissions and an endpoint.") { Name, Entity }.Handle(AddAsync);
        return new Command("module", "Manage feature modules.") { add };
    }

    // ponytail: naive singular, --entity overrides it
    public static string EntityName(string module, string? entity) =>
        entity ?? (module.Length > 1 && module.EndsWith('s') ? module[..^1] : module + "Item");

    public static IReadOnlyDictionary<string, string> Tokens(string app, string module, string entity) => new Dictionary<string, string>
    {
        ["__App__"] = app,
        ["__Module__"] = module,
        ["__Entity__"] = entity,
        ["__route__"] = module.ToLowerInvariant(),
    };

    public static IEnumerable<(string Path, string Content)> Files(IReadOnlyDictionary<string, string> tokens)
    {
        var assembly = typeof(ModuleCommand).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            yield return (Replace(resource[Prefix.Length..^".txt".Length].Replace('\\', '/'), tokens), Replace(reader.ReadToEnd(), tokens));
        }
    }

    public static string Register(string databaseModule, string moduleType)
    {
        const string Marker = "[DependsOn(";
        var index = databaseModule.IndexOf(Marker, StringComparison.Ordinal);
        return index < 0
            ? throw new CliException("No [DependsOn(...)] found in the database module; register the module there yourself.")
            : databaseModule.Insert(index + Marker.Length, $"typeof({moduleType}), ");
    }

    private static string Replace(string text, IReadOnlyDictionary<string, string> tokens) =>
        tokens.Aggregate(text, (current, token) => current.Replace(token.Key, token.Value, StringComparison.Ordinal));

    private static async Task<int> AddAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var solution = Solution.Find(Environment.CurrentDirectory);
        var module = result.GetValue(Name)!;
        var project = Path.Combine(solution.Src, $"{solution.Name}.{module}", $"{solution.Name}.{module}.csproj");
        if (File.Exists(project))
        {
            throw new CliException($"{project} exists already.");
        }

        foreach (var (path, content) in Files(Tokens(solution.Name, module, EntityName(module, result.GetValue(Entity)))))
        {
            var target = Path.Combine(solution.Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, content, cancellationToken);
        }

        var infrastructure = solution.Project("Infrastructure");
        var databaseModule = Path.Combine(Path.GetDirectoryName(infrastructure)!, $"{solution.Name}DatabaseModule.cs");
        var registered = Register(await File.ReadAllTextAsync(databaseModule, cancellationToken), $"{solution.Name}.{module}.{solution.Name}{module}Module");
        await File.WriteAllTextAsync(databaseModule, registered, cancellationToken);

        var exit = await Shell.RunAsync(["dotnet", "sln", solution.SlnxPath, "add", project], cancellationToken: cancellationToken);
        if (exit == 0)
        {
            exit = await Shell.RunAsync(["dotnet", "add", infrastructure, "reference", project], cancellationToken: cancellationToken);
        }

        Console.WriteLine($"Module {module} added. Next: coworkee migrations add Add{module}");
        return exit;
    }
}
