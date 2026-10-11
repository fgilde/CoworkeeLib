using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>
/// Writes generated modules and entities into an app and wires them into the solution, the database module and the web client.
/// Every edit is computed in memory first; files are written only when all of them worked, so a failure leaves the app as it was.
/// </summary>
internal static class FeatureWriter
{
    private const string Mapperly = "    <PackageVersion Include=\"Riok.Mapperly\" Version=\"4.3.1\" />\n";
    private const string ClientEntitiesVersion = "    <PackageVersion Include=\"Coworkee.Client.Blazor.ClientEntities\" Version=\"$(CoworkeeVersion)\" />\n";
    private const string ClientEntitiesPackage = "    <CoworkeePackage Include=\"Coworkee.Client.Blazor.ClientEntities\" />\n";

    public static async Task AddModuleAsync(Solution solution, string module, IReadOnlyList<EntityModel> entities, IReadOnlyList<EnumSpec> enums, CancellationToken cancellationToken)
    {
        var project = $"src/{solution.Name}.{module}/{solution.Name}.{module}.csproj";
        var changes = new Changes(solution, [.. EnumFiles(solution, enums), .. ServerFiles.Module(solution.Name, module, entities), .. entities.SelectMany(Files)]);
        var infrastructure = solution.Project("Infrastructure");
        var databaseModule = Path.Combine(Path.GetDirectoryName(infrastructure)!, $"{solution.Name}DatabaseModule.cs");
        await changes.EditAsync(databaseModule, text => AppEdits.RegisterModule(text, $"{solution.Name}.{module}.{ServerFiles.ModuleClass(solution.Name, module)}"), cancellationToken);
        await changes.EditAsync(solution.SlnxPath, text => AppEdits.AddToSolution(text, project), cancellationToken);
        await changes.EditAsync(infrastructure, text => AppEdits.AddToItems(text, $"    <ProjectReference Include=\"..\\{solution.Name}.{module}\\{solution.Name}.{module}.csproj\" />\n"), cancellationToken);
        await WireAsync(solution, changes, entities, cancellationToken);
        await changes.WriteAsync(cancellationToken);
    }

    public static async Task AddEntityAsync(Solution solution, EntityModel entity, IReadOnlyList<EnumSpec> enums, CancellationToken cancellationToken)
    {
        var root = Path.Combine(solution.Src, $"{solution.Name}.{entity.Module}");
        var moduleClass = Path.Combine(root, ServerFiles.ModuleClass(solution.Name, entity.Module) + ".cs");
        if (!File.Exists(moduleClass))
        {
            throw new CliException($"{moduleClass} does not exist. Create the module with coworkee module add {entity.Module}.");
        }

        var changes = new Changes(solution, [.. EnumFiles(solution, enums), .. Files(entity)]);
        var project = Path.Combine(root, $"{solution.Name}.{entity.Module}.csproj");
        await changes.EditAsync(moduleClass, text => AppEdits.AddToModule(text, ServerFiles.Services(entity), ServerFiles.Endpoint(entity)), cancellationToken);
        if (entity.Mapping == MappingStrategy.Mapperly)
        {
            await changes.EditAsync(project, text => AppEdits.AddToItems(text, ServerFiles.MapperlyReference), cancellationToken);
        }

        foreach (var module in entity.ReferencedModules)
        {
            var target = Path.Combine(solution.Src, $"{solution.Name}.{module}", $"{solution.Name}.{module}.csproj");
            if (File.Exists(target) && File.ReadAllText(target).Contains($"{solution.Name}.{entity.Module}.csproj", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliException($"{module} references {entity.Module} already; {entity.Module} cannot reference {module} back.");
            }

            await changes.EditAsync(project, text => AppEdits.AddToItems(text, ServerFiles.ModuleReference(solution.Name, module)), cancellationToken);
        }

        await WireAsync(solution, changes, [entity], cancellationToken);
        await changes.WriteAsync(cancellationToken);
    }

    public static IEnumerable<GeneratedFile> Files(EntityModel entity) => ServerFiles.Entity(entity).Concat(ClientFiles.Entity(entity));

    public static IEnumerable<GeneratedFile> EnumFiles(Solution solution, IReadOnlyList<EnumSpec> enums) => enums.Select(e => ServerFiles.Enum(solution.Name, e));

    private static async Task WireAsync(Solution solution, Changes changes, IReadOnlyList<EntityModel> entities, CancellationToken cancellationToken)
    {
        var client = solution.Project("Web.Client");
        var program = Path.Combine(Path.GetDirectoryName(client)!, "Program.cs");
        var packages = Path.Combine(solution.Root, "Directory.Packages.props");
        foreach (var entity in entities)
        {
            await changes.EditAsync(program, text => AppEdits.AddBeforeBuild(text, ClientFiles.Registration(entity)), cancellationToken);
        }

        if (entities.Any(e => e.Mapping == MappingStrategy.Mapperly))
        {
            await changes.EditAsync(packages, text => AppEdits.AddToItems(text, Mapperly), cancellationToken);
        }

        if (entities.Any(e => e.Spec.ClientEntity))
        {
            await changes.EditAsync(packages, text => AppEdits.AddToItems(text, ClientEntitiesVersion), cancellationToken);
            await changes.EditAsync(client, text => AppEdits.AddToItems(text, ClientEntitiesPackage), cancellationToken);
            await changes.EditAsync(program, AppEdits.EnableClientEntities, cancellationToken);
        }
    }

    private sealed class Changes
    {
        private readonly Solution _solution;
        private readonly IReadOnlyList<GeneratedFile> _files;
        private readonly Dictionary<string, (string Original, string Text)> _edits = new(StringComparer.OrdinalIgnoreCase);

        public Changes(Solution solution, IReadOnlyList<GeneratedFile> files)
        {
            if (files.Select(f => Path.Combine(solution.Root, f.Path)).FirstOrDefault(File.Exists) is { } existing)
            {
                throw new CliException($"{existing} exists already.");
            }

            _solution = solution;
            _files = files;
        }

        // edits work on LF line ends; the file keeps the ones it had
        public async Task EditAsync(string path, Func<string, string> edit, CancellationToken cancellationToken)
        {
            if (!_edits.TryGetValue(path, out var staged))
            {
                var original = await File.ReadAllTextAsync(path, cancellationToken);
                staged = (original, AppEdits.Normalize(original));
            }

            _edits[path] = (staged.Original, edit(staged.Text));
        }

        public async Task WriteAsync(CancellationToken cancellationToken)
        {
            foreach (var file in _files)
            {
                var target = Path.Combine(_solution.Root, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, file.Content.ReplaceLineEndings(), cancellationToken);
            }

            foreach (var (path, (original, text)) in _edits.Where(e => e.Value.Text != AppEdits.Normalize(e.Value.Original)))
            {
                await File.WriteAllTextAsync(path, original.Contains('\r') ? text.ReplaceLineEndings("\r\n") : text, cancellationToken);
            }
        }
    }
}
