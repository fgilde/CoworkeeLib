using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>The module project and, per entity, its contracts, domain, persistence, permissions, commands and endpoints.</summary>
internal static class ServerFiles
{
    public static string ModuleClass(string app, string module) => $"{app}{module}Module";

    public static IEnumerable<GeneratedFile> Module(string app, string module, IReadOnlyList<EntityModel> entities)
    {
        var root = $"src/{app}.{module}";
        yield return new($"{root}/{app}.{module}.csproj", Project(app, entities.Any(e => e.Mapping == MappingStrategy.Mapperly), entities.SelectMany(e => e.ReferencedModules).Distinct()));
        yield return new($"{root}/{ModuleClass(app, module)}.cs", $$"""
            using Coworkee.Application.Authorization;
            using Coworkee.Application.Messaging;
            using Coworkee.AspNetCore;
            using Coworkee.Core.Modularity;
            using Coworkee.Infrastructure.Persistence;
            using Coworkee.OData;
            using Coworkee.OData.GlobalSearch;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.Extensions.DependencyInjection;
            using {{app}}.{{module}}.Domain;
            using {{app}}.{{module}}.Endpoints;
            using {{app}}.{{module}}.Permissions;
            using {{app}}.{{module}}.Persistence;
            using {{app}}.Contracts.{{module}};

            namespace {{app}}.{{module}};

            [DependsOn(typeof(CoworkeeODataModule))]
            public sealed class {{ModuleClass(app, module)}} : CoworkeeModule, IWebModule
            {
                public override void ConfigureServices(ModuleServiceContext context)
                {
                    context.Services.AddMessagingFromAssembly(typeof({{ModuleClass(app, module)}}).Assembly);
            {{string.Concat(entities.Select(Services))}}    }

                public void ConfigureApplication(WebApplication app)
                {
            {{string.Concat(entities.Select(Endpoint))}}    }
            }

            """);
    }

    public static string Project(string app, bool mapperly, IEnumerable<string>? modules = null) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
            <ProjectReference Include="..\{app}.Contracts\{app}.Contracts.csproj" />
            <ProjectReference Include="..\{app}.Application\{app}.Application.csproj" />
        {string.Concat((modules ?? []).Select(m => ModuleReference(app, m)))}    <CoworkeePackage Include="Coworkee.AspNetCore" />
            <CoworkeePackage Include="Coworkee.Infrastructure" />
            <CoworkeePackage Include="Coworkee.OData" />
        {(mapperly ? MapperlyReference : string.Empty)}  </ItemGroup>
        </Project>

        """;

    public static string ModuleReference(string app, string module) => $"    <ProjectReference Include=\"..\\{app}.{module}\\{app}.{module}.csproj\" />\n";

    public const string MapperlyReference = "    <PackageReference Include=\"Riok.Mapperly\" PrivateAssets=\"all\" ExcludeAssets=\"runtime\" />\n";

    /// <summary>The lines that register an entity in ConfigureServices of its module.</summary>
    public static string Services(EntityModel e) => $"""
                context.Services.AddSingleton<IModelContributor, {e.Name}ModelContributor>();
                context.Services.AddSingleton<IPermissionDefinitionContributor, {e.Name}PermissionDefinitions>();
                context.Services.AddODataEntity<{e.Name}>("{e.Plural}", {e.Permissions}.View);
        {GlobalSearch(e)}
        """.ReplaceLineEndings("\n");

    /// <summary>Ctrl+K and /search find the entity by its text properties; a hit opens its list searched for it.</summary>
    public static string GlobalSearch(EntityModel e)
    {
        var fields = e.Properties.Where(EntityModel.IsText).Select(p => $"e => e.{p.Name}").ToList();
        return fields.Count == 0 ? string.Empty
            : $"        context.Services.AddGlobalSearch<{e.Name}>(s => s.Fields({string.Join(", ", fields)}).Page(\"{e.Route}\"));\n";
    }

    public static string Endpoint(EntityModel e) => $"        app.Map{e.Name}Endpoints();\n";

    /// <summary>An enum of the spec in the contracts; JSON and OData carry its names.</summary>
    public static GeneratedFile Enum(string app, EnumSpec spec) => new($"src/{app}.Contracts/Enums/{spec.Name}.cs", $$"""
        using System.Text.Json.Serialization;

        namespace {{app}}.Contracts;

        [JsonConverter(typeof(JsonStringEnumConverter<{{spec.Name}}>))]
        public enum {{spec.Name}}
        {
        {{string.Concat(spec.Values.Select(v => $"    {v},\n"))}}}

        """);

    public static IEnumerable<GeneratedFile> Entity(EntityModel e)
    {
        var contracts = $"src/{e.App}.Contracts/{e.Module}";
        var root = $"src/{e.App}.{e.Module}";
        var features = $"{root}/Features/{e.Plural}";
        yield return new($"{contracts}/{e.Module}Permissions.{e.Plural}.cs", Permissions(e));
        yield return new($"{contracts}/{e.Name}Dto.cs", Dto(e));
        yield return new($"{contracts}/AddEdit{e.Name}Request.cs", $$"""
            namespace {{e.App}}.Contracts.{{e.Module}};

            public sealed class AddEdit{{e.Name}}Request
            {
            {{string.Join("\n\n", e.Properties.Select(p => "    " + EntityModel.Settable(p)))}}
            }

            """);
        yield return new($"{root}/Domain/{e.Name}.cs", Domain(e));
        yield return new($"{root}/Persistence/{e.Name}ModelContributor.cs", ModelContributor(e));
        yield return new($"{root}/Permissions/{e.Name}PermissionDefinitions.cs", PermissionDefinitions(e));
        yield return new($"{features}/AddEdit{e.Name}.cs", CommandFiles.AddEdit(e));
        if (CommandFiles.Validator(e) is { } validator)
        {
            yield return new($"{features}/AddEdit{e.Name}Validator.cs", validator);
        }

        yield return new($"{features}/Delete{e.Plural}.cs", Delete(e));
        yield return new($"{features}/{e.Name}Mapping.cs", CommandFiles.Mapping(e));
        yield return new($"{root}/Endpoints/{e.Name}Endpoints.cs", Endpoints(e));
    }

    private static string Permissions(EntityModel e) => $$"""
        namespace {{e.App}}.Contracts.{{e.Module}};

        public static partial class {{e.Module}}Permissions
        {
            public static class {{e.Plural}}
            {
                public const string GroupName = "{{e.Module}}.{{e.Plural}}";
                public const string View = "{{e.Module}}.{{e.Plural}}.View";
                public const string Create = "{{e.Module}}.{{e.Plural}}.Create";
                public const string Edit = "{{e.Module}}.{{e.Plural}}.Edit";
                public const string Delete = "{{e.Module}}.{{e.Plural}}.Delete";
            }
        }

        """;

    private static string Dto(EntityModel e)
    {
        var members = e.Properties.Select(p => p.Type == PropertyType.Reference
            ? $", {EntityModel.Type(p)} {p.Name}Id, {e.Name}Dto.{p.Name}Reference? {p.Name}"
            : $", {EntityModel.Type(p)} {p.Name}");
        var body = e.References.Any()
            ? "\n{\n" + string.Join("\n", e.References.Select(p => $"    public sealed record {p.Name}Reference(Guid Id, string? {e.TargetDisplay(p)});\n")) + "}"
            : ";";
        return $$"""
            namespace {{e.App}}.Contracts.{{e.Module}};

            public sealed record {{e.Name}}Dto(Guid Id{{string.Concat(members)}}){{body}}

            """;
    }

    private static string Domain(EntityModel e)
    {
        var usings = (e.UsesEnums ? $"using {e.App}.Contracts;\n" : string.Empty) + $"using {e.App}.Contracts.{e.Module};\n"
            + string.Concat(e.ReferencedModules.Select(m => $"using {e.App}.{m}.Domain;\n"));
        var inverses = string.Concat(e.Inverses.Select(i => $"    public List<{i.Source.Name}> {i.Property.Inverse} {{ get; set; }} = [];\n\n"));
        return $$"""
            using Coworkee.Domain;
            {{usings}}
            namespace {{e.App}}.{{e.Module}}.Domain;

            [Realtime({{e.Permissions}}.View)]
            {{(e.Spec.ClientEntity ? "[ClientEntity]\n" : string.Empty)}}public sealed class {{e.Name}} : AuditedEntity, IMultiTenant
            {
            {{string.Concat(e.Properties.Select(p => DomainMember(e, p)))}}{{inverses}}    public Guid TenantId { get; set; }
            }

            """;
    }

    private static string DomainMember(EntityModel e, PropertySpec p)
    {
        var facet = !p.Facet ? string.Empty
            : p.Type == PropertyType.Reference
                ? $"    [Nextended.Core.Facets.ProvideFacet(Label = \"{EntityModel.Label(p)}\", ValuePath = \"{p.Name}Id\", LabelPath = \"{p.Name}.{e.TargetDisplay(p)}\", ValueType = typeof(Guid))]\n"
                : $"    [Nextended.Core.Facets.ProvideFacet(Label = \"{EntityModel.Label(p)}\")]\n";
        var navigation = p.Type == PropertyType.Reference ? $"    public {e.Target(p).Name}? {p.Name} {{ get; set; }}\n\n" : string.Empty;
        return $"{facet}    {EntityModel.Settable(p)}\n\n{navigation}";
    }

    private static string ModelContributor(EntityModel e) => $$"""
        using Coworkee.Infrastructure.Persistence;
        using Microsoft.EntityFrameworkCore;
        using {{e.App}}.{{e.Module}}.Domain;

        namespace {{e.App}}.{{e.Module}}.Persistence;

        internal sealed class {{e.Name}}ModelContributor : IModelContributor
        {
            public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<{{e.Name}}>(entity =>
            {
                entity.ToTable("{{e.Plural}}", "app");
        {{string.Concat(e.Properties.SelectMany(Configuration).Select(l => $"        {l}\n"))}}    });
        }

        """;

    private static IEnumerable<string> Configuration(PropertySpec p)
    {
        if (EntityModel.MaxLength(p) is { } max)
        {
            yield return $"entity.Property(e => e.{p.Name}).HasMaxLength({max});";
        }

        if (EntityModel.Precision(p) is var (precision, scale))
        {
            yield return $"entity.Property(e => e.{p.Name}).HasPrecision({precision}, {scale});";
        }

        if (p.Type == PropertyType.Reference)
        {
            var inverse = p.Inverse is { } collection ? $"t => t.{collection}" : string.Empty;
            yield return $"entity.HasOne(e => e.{p.Name}).WithMany({inverse}).HasForeignKey(e => e.{p.Name}Id).OnDelete(DeleteBehavior.{p.OnDelete ?? Spec.DeleteBehavior.Restrict});";
        }

        if (p.Unique)
        {
            yield return $"entity.HasIndex(e => new {{ e.TenantId, e.{EntityModel.Field(p)} }}).IsUnique();";
        }
    }

    private static string PermissionDefinitions(EntityModel e) => $$"""
        using Coworkee.Application.Authorization;
        using {{e.App}}.Contracts.{{e.Module}};

        namespace {{e.App}}.{{e.Module}}.Permissions;

        internal sealed class {{e.Name}}PermissionDefinitions : IPermissionDefinitionContributor
        {
            public void Define(PermissionDefinitionContext context) =>
                context.Group({{e.Permissions}}.GroupName, "{{e.Module}}: {{e.Plural}}")
                    .Add({{e.Permissions}}.View, "View {{e.PluralWords}}")
                    .Add({{e.Permissions}}.Create, "Create {{e.PluralWords}}", {{e.Permissions}}.View)
                    .Add({{e.Permissions}}.Edit, "Edit {{e.PluralWords}}", {{e.Permissions}}.View)
                    .Add({{e.Permissions}}.Delete, "Delete {{e.PluralWords}}", {{e.Permissions}}.View);
        }

        """;

    private static string Delete(EntityModel e) => $$"""
        using System.ComponentModel;
        using Coworkee.Application.Authorization;
        using Coworkee.Application.Messaging;
        using Coworkee.Core.Results;
        using Coworkee.Infrastructure.Persistence;
        using Microsoft.EntityFrameworkCore;
        using {{e.App}}.{{e.Module}}.Domain;
        using {{e.App}}.Contracts.{{e.Module}};

        namespace {{e.App}}.{{e.Module}}.Features.{{e.Plural}};

        [Description("Deletes {{e.PluralWords}} by id.")]
        [RequiresPermission({{e.Permissions}}.Delete)]
        public sealed record Delete{{e.Plural}}Command(IReadOnlyList<Guid> Ids) : ICommand<Result>;

        internal sealed class Delete{{e.Plural}}Handler(CoworkeeDbContext db) : IHandler<Delete{{e.Plural}}Command, Result>
        {
            public async Task<Result> HandleAsync(Delete{{e.Plural}}Command command, CancellationToken cancellationToken)
            {
                var entities = await db.Set<{{e.Name}}>().Where(e => command.Ids.Contains(e.Id)).ToListAsync(cancellationToken);
                if (entities.Count != command.Ids.Distinct().Count())
                {
                    return Error.NotFound("{{e.ErrorCode}}_not_found", "The {{e.Words}} does not exist.");
                }

                db.RemoveRange(entities);
                return Result.Success();
            }
        }

        """;

    private static string Endpoints(EntityModel e) => $$"""
        using Coworkee.Application.Messaging;
        using Coworkee.AspNetCore.Http;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using {{e.App}}.{{e.Module}}.Features.{{e.Plural}};
        using {{e.App}}.Contracts;
        using {{e.App}}.Contracts.{{e.Module}};

        namespace {{e.App}}.{{e.Module}}.Endpoints;

        internal static class {{e.Name}}Endpoints
        {
            public static void Map{{e.Name}}Endpoints(this IEndpointRouteBuilder app)
            {
                var group = app.MapCoworkeeApi("/{{e.ApiRoute}}").WithTags("{{e.Plural}}").RequireAuthorization();
                group.MapPost("/", (AddEdit{{e.Name}}Request body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddEdit{{e.Name}}Command(null, body), ct).ToHttpResult());
                group.MapPut("/{id:guid}", (Guid id, AddEdit{{e.Name}}Request body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddEdit{{e.Name}}Command(id, body), ct).ToHttpResult());
                group.MapPost("/delete", (IdsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new Delete{{e.Plural}}Command(body.Ids), ct).ToHttpResult());
            }
        }

        """;
}
