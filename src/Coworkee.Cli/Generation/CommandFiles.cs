using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>The add/edit command with its checks, the validator and the DTO mapping of an entity.</summary>
internal static class CommandFiles
{
    public static string AddEdit(EntityModel e) => $$"""
        using System.ComponentModel;
        using Coworkee.Application.Authorization;
        using Coworkee.Application.Messaging;
        using Coworkee.Core.Results;
        using Coworkee.Infrastructure.Persistence;
        using Microsoft.EntityFrameworkCore;
        using {{e.App}}.{{e.Module}}.Domain;
        using {{e.App}}.Contracts.{{e.Module}};
        {{string.Concat(e.ReferencedModules.Select(m => $"using {e.App}.{m}.Domain;\n"))}}
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

        {{string.Concat(e.References.Select(p => Lookup(e, p)))}}        var entity = command.Id is { } id ? await db.Set<{{e.Name}}>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken) : Add();
                if (entity is null)
                {
                    return Error.NotFound("{{e.ErrorCode}}_not_found", "The {{e.Words}} does not exist.");
                }

        {{string.Concat(e.Properties.Select(Assignment))}}{{string.Concat(e.Properties.Where(p => p.Unique).Select(p => UniqueCheck(e, p)))}}        return entity.ToDto();
            }

            private {{e.Name}} Add()
            {
                var entity = new {{e.Name}}();
                db.Add(entity);
                return entity;
            }
        }

        """;

    public static string? Validator(EntityModel e)
    {
        var rules = e.Properties.Select(p => (Field: EntityModel.Field(p), Rule: Rule(p))).Where(r => r.Rule.Length > 0).ToList();
        var urls = e.Properties.Any(p => p.Type == PropertyType.Url);
        return rules.Count == 0 ? null : $$"""
            using FluentValidation;

            namespace {{e.App}}.{{e.Module}}.Features.{{e.Plural}};

            internal sealed class AddEdit{{e.Name}}Validator : AbstractValidator<AddEdit{{e.Name}}Command>
            {
                public AddEdit{{e.Name}}Validator()
                {
            {{string.Concat(rules.Select(r => $"        RuleFor(c => c.Request.{r.Field}){r.Rule};\n"))}}    }
            {{(urls ? WebAddress : string.Empty)}}}

            """;
    }

    public static string Mapping(EntityModel e) => e.Mapping == MappingStrategy.Mapperly
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
                public static {{e.Name}}Dto ToDto(this {{e.Name}} entity) => new(entity.Id{{string.Concat(e.Properties.Select(p => ", " + Mapped(e, p)))}});
            }

            """;

    private const string WebAddress = """

            private static bool BeAWebAddress(string? value) =>
                string.IsNullOrEmpty(value) || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

        """;

    private static string Mapped(EntityModel e, PropertySpec p) => p.Type != PropertyType.Reference
        ? $"entity.{p.Name}"
        : $"entity.{p.Name}Id, entity.{p.Name} is null ? null : new {e.Name}Dto.{p.Name}Reference(entity.{p.Name}.Id, entity.{p.Name}.{e.TargetDisplay(p)})";

    // the referenced row must exist in the tenant; it also fills the navigation for the DTO
    private static string Lookup(EntityModel e, PropertySpec p)
    {
        var target = e.Target(p).Name;
        var found = $"referenced{p.Name}";
        var query = p.Required
            ? $"await db.Set<{target}>().SingleOrDefaultAsync(r => r.Id == command.Request.{p.Name}Id, cancellationToken)"
            : $"command.Request.{p.Name}Id is {{ }} {EntityModel.Camel(p.Name)}Id ? await db.Set<{target}>().SingleOrDefaultAsync(r => r.Id == {EntityModel.Camel(p.Name)}Id, cancellationToken) : null";
        var missing = p.Required ? $"{found} is null" : $"command.Request.{p.Name}Id is not null && {found} is null";
        return $$"""
                    var {{found}} = {{query}};
                    if ({{missing}})
                    {
                        return Error.Validation("{{p.Name}}Id", "The {{EntityModel.Humanize(target)}} does not exist.");
                    }


            """;
    }

    private static string Assignment(PropertySpec p) => p.Type == PropertyType.Reference
        ? $"        entity.{p.Name}Id = command.Request.{p.Name}Id;\n        entity.{p.Name} = referenced{p.Name};\n"
        : $"        entity.{p.Name} = command.Request.{p.Name}{Trim(p)};\n";

    private static string Trim(PropertySpec property) => !EntityModel.IsText(property) ? string.Empty : property.Required ? ".Trim()" : "?.Trim()";

    private static string UniqueCheck(EntityModel e, PropertySpec p)
    {
        var field = EntityModel.Field(p);
        var guard = p.Required ? string.Empty : $"entity.{field} is not null && ";
        return $$"""
                    if ({{guard}}await db.Set<{{e.Name}}>().AnyAsync(e => e.Id != entity.Id && e.{{field}} == entity.{{field}}, cancellationToken))
                    {
                        return Error.Conflict("{{e.ErrorCode}}_{{EntityModel.Snake(p.Name)}}_taken", "Another {{e.Words}} has this {{EntityModel.Humanize(p.Name)}}.");
                    }


            """;
    }

    private static string Rule(PropertySpec p)
    {
        var rule = (p.Required && (EntityModel.IsText(p) || p.Type == PropertyType.Reference) ? ".NotEmpty()" : string.Empty)
            + (EntityModel.MaxLength(p) is { } max ? $".MaximumLength({max})" : string.Empty)
            + (p.Type == PropertyType.Enum ? ".IsInEnum()" : string.Empty)
            + (p.Type == PropertyType.Url ? ".Must(BeAWebAddress).WithMessage(\"Enter an http or https address.\")" : string.Empty);
        return p.Type != PropertyType.Email ? rule
            : p.Required ? rule + ".EmailAddress()"
            : $"{rule}.EmailAddress().When(c => !string.IsNullOrEmpty(c.Request.{p.Name}))";
    }
}
