using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>The module project and, per entity, its contracts, domain, persistence, permissions, commands and endpoints.</summary>
internal static class ServerFiles
{
    public static string ModuleClass(string app, string module) => $"{app}{module}Module";

    public static IEnumerable<GeneratedFile> Module(string app, string module, IReadOnlyList<EntityModel> entities)
    {
        var root = $"src/{app}.{module}";
        yield return new($"{root}/{app}.{module}.csproj", Project(app, entities.Any(e => e.Mapping == MappingStrategy.Mapperly)));
        yield return new($"{root}/{ModuleClass(app, module)}.cs", $$"""
            using Coworkee.Application.Authorization;
            using Coworkee.Application.Messaging;
            using Coworkee.AspNetCore;
            using Coworkee.Core.Modularity;
            using Coworkee.Infrastructure.Persistence;
            using Coworkee.OData;
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

    public static string Project(string app, bool mapperly) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
            <ProjectReference Include="..\{app}.Contracts\{app}.Contracts.csproj" />
            <ProjectReference Include="..\{app}.Application\{app}.Application.csproj" />
            <CoworkeePackage Include="Coworkee.AspNetCore" />
            <CoworkeePackage Include="Coworkee.Infrastructure" />
            <CoworkeePackage Include="Coworkee.OData" />
        {(mapperly ? MapperlyReference : string.Empty)}  </ItemGroup>
        </Project>

        """;

    public const string MapperlyReference = "    <PackageReference Include=\"Riok.Mapperly\" PrivateAssets=\"all\" ExcludeAssets=\"runtime\" />\n";

    /// <summary>The lines that register an entity in ConfigureServices of its module.</summary>
    public static string Services(EntityModel e) => $"""
                context.Services.AddSingleton<IModelContributor, {e.Name}ModelContributor>();
                context.Services.AddSingleton<IPermissionDefinitionContributor, {e.Name}PermissionDefinitions>();
                context.Services.AddODataEntity<{e.Name}>("{e.Plural}", {e.Permissions}.View);

        """.ReplaceLineEndings("\n");

    public static string Endpoint(EntityModel e) => $"        app.Map{e.Name}Endpoints();\n";

    public static IEnumerable<GeneratedFile> Entity(EntityModel e)
    {
        var contracts = $"src/{e.App}.Contracts/{e.Module}";
        var root = $"src/{e.App}.{e.Module}";
        var features = $"{root}/Features/{e.Plural}";
        yield return new($"{contracts}/{e.Module}Permissions.{e.Plural}.cs", Permissions(e));
        yield return new($"{contracts}/{e.Name}Dto.cs", $$"""
            namespace {{e.App}}.Contracts.{{e.Module}};

            public sealed record {{e.Name}}Dto(Guid Id{{string.Concat(e.Properties.Select(p => $", {EntityModel.Type(p)} {p.Name}"))}});

            """);
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
        yield return new($"{features}/AddEdit{e.Name}.cs", AddEdit(e));
        if (Validator(e) is { } validator)
        {
            yield return new($"{features}/AddEdit{e.Name}Validator.cs", validator);
        }

        yield return new($"{features}/Delete{e.Plural}.cs", Delete(e));
        yield return new($"{features}/{e.Name}Mapping.cs", Mapping(e));
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

    private static string Domain(EntityModel e) => $$"""
        using Coworkee.Domain;
        using {{e.App}}.Contracts.{{e.Module}};

        namespace {{e.App}}.{{e.Module}}.Domain;

        [Realtime({{e.Permissions}}.View)]
        {{(e.Spec.ClientEntity ? "[ClientEntity]\n" : string.Empty)}}public sealed class {{e.Name}} : AuditedEntity, IMultiTenant
        {
        {{string.Concat(e.Properties.Select(p => $"    {EntityModel.Settable(p)}\n\n"))}}    public Guid TenantId { get; set; }
        }

        """;

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
        {{string.Concat(e.Properties.Where(p => p.MaxLength is not null).Select(p => $"        entity.Property(e => e.{p.Name}).HasMaxLength({p.MaxLength});\n"))}}    });
        }

        """;

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

    private static string AddEdit(EntityModel e) => $$"""
        using System.ComponentModel;
        using Coworkee.Application.Authorization;
        using Coworkee.Application.Messaging;
        using Coworkee.Core.Results;
        using Coworkee.Infrastructure.Persistence;
        using Microsoft.EntityFrameworkCore;
        using {{e.App}}.{{e.Module}}.Domain;
        using {{e.App}}.Contracts.{{e.Module}};

        namespace {{e.App}}.{{e.Module}}.Features.{{e.Plural}};

        [Description("Creates a {{e.Words}} or changes the one with the id.")]
        public sealed record AddEdit{{e.Name}}Command(Guid? Id, AddEdit{{e.Name}}Request Request) : ICommand<Result<{{e.Name}}Dto>>;

        internal sealed class AddEdit{{e.Name}}Handler(CoworkeeDbContext db, IPermissionChecker permissions) : IHandler<AddEdit{{e.Name}}Command, Result<{{e.Name}}Dto>>
        {
            public async Task<Result<{{e.Name}}Dto>> HandleAsync(AddEdit{{e.Name}}Command command, CancellationToken cancellationToken)
            {
                if (!await permissions.IsGrantedAsync(command.Id is null ? {{e.Permissions}}.Create : {{e.Permissions}}.Edit, cancellationToken))
                {
                    return Error.Forbidden("{{e.ErrorCode}}_forbidden", "You may not do this.");
                }

                var entity = command.Id is { } id ? await db.Set<{{e.Name}}>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken) : Add();
                if (entity is null)
                {
                    return Error.NotFound("{{e.ErrorCode}}_not_found", "The {{e.Words}} does not exist.");
                }

        {{string.Concat(e.Properties.Select(p => $"        entity.{p.Name} = command.Request.{p.Name}{Trim(p)};\n"))}}        return entity.ToDto();
            }

            private {{e.Name}} Add()
            {
                var entity = new {{e.Name}}();
                db.Add(entity);
                return entity;
            }
        }

        """;

    private static string Trim(PropertySpec property) => property.Type != PropertyType.String ? string.Empty : property.Required ? ".Trim()" : "?.Trim()";

    private static string? Validator(EntityModel e)
    {
        var rules = e.Properties
            .Select(p => (p.Name, Rule: (p is { Type: PropertyType.String, Required: true } ? ".NotEmpty()" : string.Empty) + (p.MaxLength is { } max ? $".MaximumLength({max})" : string.Empty)))
            .Where(r => r.Rule.Length > 0)
            .ToList();
        return rules.Count == 0 ? null : $$"""
            using FluentValidation;

            namespace {{e.App}}.{{e.Module}}.Features.{{e.Plural}};

            internal sealed class AddEdit{{e.Name}}Validator : AbstractValidator<AddEdit{{e.Name}}Command>
            {
                public AddEdit{{e.Name}}Validator()
                {
            {{string.Concat(rules.Select(r => $"        RuleFor(c => c.Request.{r.Name}){r.Rule};\n"))}}    }
            }

            """;
    }

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

    private static string Mapping(EntityModel e) => e.Mapping == MappingStrategy.Mapperly
        ? $$"""
            using Riok.Mapperly.Abstractions;
            using {{e.App}}.{{e.Module}}.Domain;
            using {{e.App}}.Contracts.{{e.Module}};

            namespace {{e.App}}.{{e.Module}}.Features.{{e.Plural}};

            [Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
            internal static partial class {{e.Name}}Mapping
            {
                public static partial {{e.Name}}Dto ToDto(this {{e.Name}} entity);
            }

            """
        : $$"""
            using {{e.App}}.{{e.Module}}.Domain;
            using {{e.App}}.Contracts.{{e.Module}};

            namespace {{e.App}}.{{e.Module}}.Features.{{e.Plural}};

            internal static class {{e.Name}}Mapping
            {
                public static {{e.Name}}Dto ToDto(this {{e.Name}} entity) => new(entity.Id{{string.Concat(e.Properties.Select(p => $", entity.{p.Name}"))}});
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
