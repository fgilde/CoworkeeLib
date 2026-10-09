using System.Reflection;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private static readonly (string Add, Func<string, bool> Matches)[] Roles =
    [
        (nameof(AddMigrations), n => n.EndsWith("_Migrations", StringComparison.Ordinal)),
        (nameof(AddAuthServer), n => n.EndsWith("_Auth", StringComparison.Ordinal)),
        (nameof(AddApi), n => n.EndsWith("_Api", StringComparison.Ordinal)),
        (nameof(AddWorker), n => n.Contains("_Worker", StringComparison.Ordinal) || n.EndsWith("Worker", StringComparison.Ordinal)),
        (nameof(AddWeb), n => n.EndsWith("_Web", StringComparison.Ordinal)),
    ];

    /// <summary>
    /// Adds the projects the app host references (<c>Projects.MyApp_Migrations</c>, <c>_Auth</c>, <c>_Api</c>, <c>_Worker</c>, <c>_Web</c>) in that order;
    /// the suffix is the name after the first underscore (<c>MyApp_Jobs_Worker</c> becomes <c>myapp-jobs-worker</c>). Projects added before are left alone.
    /// </summary>
    public CoworkeeApp AddProjects()
    {
        var projects = (Builder.AppHostAssembly?.GetTypes() ?? [])
            .Where(t => t is { Namespace: "Projects", IsAbstract: false } && typeof(IProjectMetadata).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Where(t => !_added.Contains(t))
            .Select(t => (Type: t, Suffix: Suffix(t.Name), Role: Array.FindIndex(Roles, r => r.Matches(t.Name))))
            .Where(p => p.Role >= 0 && !Options.Skipped.Contains(p.Suffix) && !Options.Skipped.Contains(p.Type.Name))
            .OrderBy(p => p.Role)
            .ThenBy(p => p.Type.Name, StringComparer.Ordinal);
        foreach (var (type, suffix, role) in projects.ToList())
        {
            typeof(CoworkeeApp).GetMethod(Roles[role].Add)!.MakeGenericMethod(type).Invoke(this, BindingFlags.DoNotWrapExceptions, null, [suffix], null);
        }

        return this;
    }

    private static string Suffix(string typeName) =>
        typeName[(typeName.IndexOf('_', StringComparison.Ordinal) + 1)..].Replace('_', '-').ToLowerInvariant();
}
