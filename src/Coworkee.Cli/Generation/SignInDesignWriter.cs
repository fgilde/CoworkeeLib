using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>
/// The sign-in options the app host does not set (fills, surface, animation, tilt, logo size, darkening) go into Coworkee:Auth:Design of the
/// appsettings.json of the auth server and of the api (which hosts the design settings and the Keycloak theme), merged with what is there.
/// </summary>
internal static class SignInDesignWriter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void Write(Solution solution, IReadOnlyDictionary<string, string?> values)
    {
        if (SignInStyle.DesignSettings(values) is not { } design)
        {
            return;
        }

        foreach (var project in new[] { "Auth", "Api" })
        {
            Merge(Path.Combine(Path.GetDirectoryName(solution.Project(project))!, "appsettings.json"), design);
        }
    }

    internal static void Merge(string file, JsonObject design)
    {
        var text = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
        var root = string.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
        var target = Section(Section(Section(root, "Coworkee"), "Auth"), "Design");
        foreach (var (key, value) in design)
        {
            target[key] = value?.DeepClone();
        }

        File.WriteAllText(file, root.ToJsonString(Indented) + Environment.NewLine);
    }

    private static JsonObject Section(JsonObject parent, string name) =>
        parent[name] as JsonObject ?? (JsonObject)(parent[name] = new JsonObject());
}
