using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Modules;

/// <summary>The Coworkee packages a project uses (also transitively), read from its restore output; <see cref="CoworkeeApp.Modules"/> wires them.</summary>
public static class CoworkeeModules
{
    public const string Infrastructure = "Coworkee.Infrastructure";
    public const string Realtime = "Coworkee.Realtime";
    public const string Mailing = "Coworkee.Mailing";
    public const string Storage = "Coworkee.Storage";
    public const string Search = "Coworkee.Search.Elasticsearch";
    public const string BackgroundJobs = "Coworkee.BackgroundJobs";
    public const string Notifications = "Coworkee.Notifications";
    public const string Account = "Coworkee.Account";

    public static IReadOnlySet<string> Of(ProjectResource project) => Of(project.GetProjectMetadata().ProjectPath);

    public static IReadOnlySet<string> Of(string projectPath) =>
        References(projectPath).Where(n => n.StartsWith("Coworkee.", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>All packages and projects a project uses, also transitively.</summary>
    public static IReadOnlySet<string> References(string projectPath)
    {
        var assets = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
        if (!File.Exists(assets))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(assets));
        return document.RootElement.TryGetProperty("libraries", out var libraries)
            ? libraries.EnumerateObject().Select(l => l.Name.Split('/')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
