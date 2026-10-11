using System.CommandLine;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

internal static class ModuleCommand
{
    private static readonly Argument<string> Name = Names.Simple("name", "Name of the module, for example Orders.");
    private static readonly Option<string?> Entity = new("--entity") { Description = "Name of its first entity (default: the module name without a trailing s)." };

    public static Command Create()
    {
        var add = new Command("add", "Add a feature module with an entity: table, permissions, OData set, endpoints, validation and a Blazor page.")
        {
            Name, Entity, FeatureOptions.Properties, FeatureOptions.Enums, FeatureOptions.Plural, FeatureOptions.ClientEntity, FeatureOptions.Mapping,
        }.Handle(AddAsync);
        return new Command("module", "Manage feature modules.") { add };
    }

    // ponytail: naive singular, --entity overrides it
    public static string EntityName(string module, string? entity) =>
        entity ?? (module.Length > 1 && module.EndsWith('s') ? module[..^1] : module + "Item");

    public static ModuleSpec Read(ParseResult result)
    {
        var module = result.GetValue(Name)!;
        return new ModuleSpec { Name = module, Entities = [FeatureOptions.Entity(result, EntityName(module, result.GetValue(Entity)))] };
    }

    public static async Task<int> AddAsync(Solution solution, ModuleSpec module, CancellationToken cancellationToken, IReadOnlyList<EnumSpec>? enums = null)
    {
        var spec = new AppSpec { Name = solution.Name, Options = { ["samples"] = solution.HasSamples }, Modules = [module], Enums = [.. enums ?? []] };
        var index = ExistingModel.Index(solution, spec);
        FeatureOptions.Validate(spec, index);
        var entities = module.Entities.Select(e => new EntityModel(solution.Name, module.Name, e, e.Mapping ?? MappingStrategy.Manual, index)).ToList();
        await FeatureWriter.AddModuleAsync(solution, module.Name, entities, spec.Enums, cancellationToken);
        Console.WriteLine($"Module {module.Name} added. Next: coworkee migrations add Add{module.Name}");
        return 0;
    }

    private static Task<int> AddAsync(ParseResult result, CancellationToken cancellationToken) =>
        AddAsync(Solution.Find(Environment.CurrentDirectory), Read(result), cancellationToken, FeatureOptions.EnumSpecs(result));
}
