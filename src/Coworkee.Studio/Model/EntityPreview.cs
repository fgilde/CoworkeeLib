using Coworkee.Cli.Spec;

namespace Coworkee.Studio.Model;

/// <summary>Names the CLI generates for an entity: page route, API route, permissions and files.</summary>
// ponytail: mirrors the naming of Coworkee.Cli's generator (EntityModel, ServerFiles, ClientFiles); a CLI command that lists the files would remove the copy
public sealed record EntityPreview(string Route, string ApiRoute, IReadOnlyList<string> Permissions, IReadOnlyList<string> Files)
{
    public static EntityPreview For(string app, string module, EntitySpec entity)
    {
        var plural = entity.PluralName;
        var root = $"src/{app}.{module}";
        var contracts = $"src/{app}.Contracts/{module}";
        var client = $"src/{app}.Web.Client";
        return new(
            $"/{Kebab(module)}/{Kebab(plural)}",
            $"api/v1/{Kebab(plural)}",
            ["View", "Create", "Edit", "Delete"].Select(p => $"{module}.{plural}.{p}").ToList(),
            [
                $"{contracts}/{entity.Name}Dto.cs",
                $"{contracts}/AddEdit{entity.Name}Request.cs",
                $"{contracts}/{module}Permissions.{plural}.cs",
                $"{root}/Domain/{entity.Name}.cs",
                $"{root}/Persistence/{entity.Name}ModelContributor.cs",
                $"{root}/Permissions/{entity.Name}PermissionDefinitions.cs",
                $"{root}/Features/{plural}/AddEdit{entity.Name}.cs",
                $"{root}/Features/{plural}/Delete{plural}.cs",
                $"{root}/Features/{plural}/{entity.Name}Mapping.cs",
                $"{root}/Endpoints/{entity.Name}Endpoints.cs",
                $"{client}/Pages/{module}/{plural}Page.razor",
                $"{client}/Api/{plural}Api.cs",
                $"{client}/Navigation/{plural}Navigation.cs",
            ]);
    }

    private static string Kebab(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
