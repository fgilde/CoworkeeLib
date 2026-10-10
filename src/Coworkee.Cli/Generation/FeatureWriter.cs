using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>Writes generated modules and entities into an app and wires them into the solution, the database module and the web client.</summary>
internal static class FeatureWriter
{
    private const string Mapperly = "    <PackageVersion Include=\"Riok.Mapperly\" Version=\"4.3.1\" />\n";
    private const string ClientEntitiesVersion = "    <PackageVersion Include=\"Coworkee.Client.Blazor.ClientEntities\" Version=\"$(CoworkeeVersion)\" />\n";
    private const string ClientEntitiesPackage = "    <CoworkeePackage Include=\"Coworkee.Client.Blazor.ClientEntities\" />\n";

    public static async Task AddModuleAsync(Solution solution, string module, IReadOnlyList<EntityModel> entities, CancellationToken cancellationToken)
    {
        var project = $"src/{solution.Name}.{module}/{solution.Name}.{module}.csproj";
        await WriteAsync(solution, [.. ServerFiles.Module(solution.Name, module, entities), .. entities.SelectMany(Files)], cancellationToken);

        var infrastructure = solution.Project("Infrastructure");
        var databaseModule = Path.Combine(Path.GetDirectoryName(infrastructure)!, $"{solution.Name}DatabaseModule.cs");
        await EditAsync(databaseModule, text => AppEdits.RegisterModule(text, $"{solution.Name}.{module}.{ServerFiles.ModuleClass(solution.Name, module)}"), cancellationToken);
        await EditAsync(solution.SlnxPath, text => AppEdits.AddToSolution(text, project), cancellationToken);
        await EditAsync(infrastructure, text => AppEdits.AddToItems(text, $"    <ProjectReference Include=\"..\\{solution.Name}.{module}\\{solution.Name}.{module}.csproj\" />\n"), cancellationToken);
        await WireAsync(solution, entities, cancellationToken);
    }

    public static async Task AddEntityAsync(Solution solution, EntityModel entity, CancellationToken cancellationToken)
    {
        var root = Path.Combine(solution.Src, $"{solution.Name}.{entity.Module}");
        var moduleClass = Path.Combine(root, ServerFiles.ModuleClass(solution.Name, entity.Module) + ".cs");
        if (!File.Exists(moduleClass))
        {
            throw new CliException($"{moduleClass} does not exist. Create the module with coworkee module add {entity.Module}.");
        }

        await WriteAsync(solution, [.. Files(entity)], cancellationToken);
        await EditAsync(moduleClass, text => AppEdits.AddToModule(text, ServerFiles.Services(entity), ServerFiles.Endpoint(entity)), cancellationToken);
        if (entity.Mapping == MappingStrategy.Mapperly)
        {
            await EditAsync(Path.Combine(root, $"{solution.Name}.{entity.Module}.csproj"), text => AppEdits.AddToItems(text, ServerFiles.MapperlyReference), cancellationToken);
        }

        await WireAsync(solution, [entity], cancellationToken);
    }

    public static IEnumerable<GeneratedFile> Files(EntityModel entity) => ServerFiles.Entity(entity).Concat(ClientFiles.Entity(entity));

    private static async Task WireAsync(Solution solution, IReadOnlyList<EntityModel> entities, CancellationToken cancellationToken)
    {
        var client = solution.Project("Web.Client");
        var program = Path.Combine(Path.GetDirectoryName(client)!, "Program.cs");
        var packages = Path.Combine(solution.Root, "Directory.Packages.props");
        foreach (var entity in entities)
        {
            await EditAsync(program, text => AppEdits.AddBeforeBuild(text, ClientFiles.Registration(entity)), cancellationToken);
        }

        if (entities.Any(e => e.Mapping == MappingStrategy.Mapperly))
        {
            await EditAsync(packages, text => AppEdits.AddToItems(text, Mapperly), cancellationToken);
        }

        if (entities.Any(e => e.Spec.ClientEntity))
        {
            await EditAsync(packages, text => AppEdits.AddToItems(text, ClientEntitiesVersion), cancellationToken);
            await EditAsync(client, text => AppEdits.AddToItems(text, ClientEntitiesPackage), cancellationToken);
            await EditAsync(program, AppEdits.EnableClientEntities, cancellationToken);
        }
    }

    private static async Task WriteAsync(Solution solution, IReadOnlyList<GeneratedFile> files, CancellationToken cancellationToken)
    {
        if (files.Select(f => Path.Combine(solution.Root, f.Path)).FirstOrDefault(File.Exists) is { } existing)
        {
            throw new CliException($"{existing} exists already.");
        }

        foreach (var file in files)
        {
            var target = Path.Combine(solution.Root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, file.Content, cancellationToken);
        }
    }

    private static async Task EditAsync(string path, Func<string, string> edit, CancellationToken cancellationToken)
    {
        var text = AppEdits.Normalize(await File.ReadAllTextAsync(path, cancellationToken));
        var changed = edit(text);
        if (changed != text)
        {
            await File.WriteAllTextAsync(path, changed, cancellationToken);
        }
    }
}
