using System.CommandLine;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

internal static class EntityCommand
{
    private static readonly Argument<string> Module = Names.Simple("module", "The module that gets the entity, created with coworkee module add.");
    private static readonly Argument<string> Name = Names.Simple("name", "Name of the entity in singular, for example Invoice.");

    public static Command Create()
    {
        var add = new Command("add", "Add an entity to a generated module: table, permissions, OData set, endpoints, validation and a Blazor page.")
        {
            Module, Name, FeatureOptions.Properties, FeatureOptions.Enums, FeatureOptions.Plural, FeatureOptions.ClientEntity, FeatureOptions.Mapping, FeatureOptions.CodeGen,
        }.Handle(AddAsync);
        return new Command("entity", "Manage entities of feature modules.") { add, EntityImportCommand.Create() };
    }

    /// <summary>Writes the entity; with <paramref name="codeGen"/> its DTO comes from Coworkee.CodeGen (build the module once afterwards: <see cref="CodeGenFiles.PrimeAsync"/>).</summary>
    public static async Task<int> AddAsync(Solution solution, string module, EntitySpec entity, CancellationToken cancellationToken, IReadOnlyList<EnumSpec>? enums = null, bool codeGen = false)
    {
        var spec = new AppSpec { Name = solution.Name, Options = { ["samples"] = solution.HasSamples }, Modules = [new ModuleSpec { Name = module, Entities = [entity] }], Enums = [.. enums ?? []] };
        var index = ExistingModel.Index(solution, spec);
        FeatureOptions.Validate(spec, index);
        await FeatureWriter.AddEntityAsync(solution, new EntityModel(solution.Name, module, entity, entity.Mapping ?? MappingStrategy.Manual, index, codeGen), spec.Enums, cancellationToken);
        Console.WriteLine($"Entity {entity.Name} added to {module}. Next: coworkee migrations add Add{entity.Name}");
        return 0;
    }

    private static async Task<int> AddAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var solution = Solution.Find(Environment.CurrentDirectory);
        var (module, entity, codeGen) = (result.GetValue(Module)!, FeatureOptions.Entity(result, result.GetValue(Name)!), result.GetValue(FeatureOptions.CodeGen));
        await AddAsync(solution, module, entity, cancellationToken, FeatureOptions.EnumSpecs(result), codeGen);
        if (codeGen)
        {
            await CodeGenFiles.PrimeAsync(solution, module, [entity.Name], cancellationToken);
        }

        return 0;
    }
}
