using System.Text.Json.Nodes;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Apps;

/// <summary>
/// What an app was created with, in <c>.coworkee/app.json</c> of the solution: written by <c>coworkee new</c> and <c>coworkee add</c>.
/// Every option that differs from its default is in <see cref="AppSpec.Options"/>; development passwords are never stored.
/// </summary>
internal static class AppState
{
    public static string PathOf(string root) => Path.Combine(root, ".coworkee", "app.json");

    /// <summary>The stored spec, or the one <see cref="AppInference"/> reads from the files of an older app.</summary>
    public static (AppSpec Spec, bool Inferred) Read(Solution solution)
    {
        var file = PathOf(solution.Root);
        return File.Exists(file) ? (SpecJson.Read(File.ReadAllText(file)), false) : (AppInference.Infer(solution), true);
    }

    public static void Write(string root, AppSpec spec)
    {
        Directory.CreateDirectory(Path.Combine(root, ".coworkee"));
        File.WriteAllText(PathOf(root), SpecJson.Write(Stored(spec)));
    }

    public static AppSpec Stored(AppSpec spec)
    {
        var values = SpecOptions.Resolve(spec);
        var stored = new AppSpec
        {
            Name = spec.Name,
            Modules = spec.Modules,
            Enums = spec.Enums,
            Roles = spec.Roles,
            Users = [.. spec.Users.Select(u => new UserSpec { Email = u.Email, DisplayName = u.DisplayName, Roles = u.Roles, Admin = u.Admin })],
        };
        foreach (var option in OptionCatalog.All)
        {
            if (values[option.Id] is { } value && value != option.Default)
            {
                stored.Options[option.Id] = Node(option, Assets.TemplateValue(option.Id, value));
            }
        }

        return stored;
    }

    private static JsonNode Node(CliOption option, string value) => option.Kind switch
    {
        OptionKind.Bool => JsonValue.Create(value == "true"),
        OptionKind.Multi => new JsonArray([.. SpecOptions.List(value).Select(v => (JsonNode)JsonValue.Create(v))]),
        _ => JsonValue.Create(value),
    };
}
