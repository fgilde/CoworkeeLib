using System.Text.RegularExpressions;

namespace Coworkee.Cli.Spec;

public sealed record SignInUser(string Email, string Password, bool IsAdmin, IReadOnlyList<string> Roles)
{
    public string RoleText => string.Join(", ", (IsAdmin ? ["Administrator"] : Array.Empty<string>()).Concat(Roles));
}

/// <summary>How to sign in to a generated app: its development users from src/*.Migrations/DemoUsers.cs and whether Keycloak takes them too.</summary>
public sealed partial record SignInInfo(string Name, IReadOnlyList<SignInUser> Users, bool Keycloak)
{
    public static SignInInfo? Read(string appFolder)
    {
        var users = Directory.Exists(Path.Combine(appFolder, "src"))
            ? Directory.EnumerateFiles(Path.Combine(appFolder, "src"), "DemoUsers.cs", SearchOption.AllDirectories).FirstOrDefault(f => Path.GetDirectoryName(f)!.EndsWith(".Migrations", StringComparison.Ordinal))
            : null;
        if (users is null)
        {
            return null;
        }

        var name = Path.GetFileName(Path.GetDirectoryName(users)!)[..^".Migrations".Length];
        var appHost = Path.Combine(appFolder, "src", $"{name}.AppHost", "AppHost.cs");
        var code = File.Exists(appHost) ? File.ReadAllText(appHost) : string.Empty;
        return new SignInInfo(name, Parse(File.ReadAllText(users)), code.Contains("UseKeycloak", StringComparison.Ordinal))
        {
            Tools = [.. new[] { ("UseGrafana", "Grafana"), ("UseWebDataStudio", "WebDataStudio"), ("UseKibana", "Kibana"), ("AddAspireUI", "AspireUI") }.Where(t => code.Contains(t.Item1, StringComparison.Ordinal)).Select(t => t.Item2)],
        };
    }

    /// <summary>Development tools of the app host (Grafana, WebDataStudio, Kibana, AspireUI); they take the administrator's sign-in.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    public SignInUser? Administrator => Users.FirstOrDefault(u => u.IsAdmin);

    public static IReadOnlyList<SignInUser> Parse(string demoUsers) =>
    [
        .. User().Matches(demoUsers).Select(m => new SignInUser(m.Groups["email"].Value, m.Groups["password"].Value, m.Groups["admin"].Value == "true",
            [.. Quoted().Matches(m.Groups["roles"].Value).Select(r => r.Groups[1].Value)])),
    ];

    /// <summary>The Aspire resource of the web app, e.g. shop-web.</summary>
    public string WebResource => $"{Name.ToLowerInvariant()}-web";

    [GeneratedRegex("""new\("(?<email>[^"]+)", "[^"]*", (?:null|"[^"]*"), "(?<password>[^"]+)"(?:, IsAdmin: (?<admin>true|false))?(?:, Roles: \[(?<roles>[^\]]*)\])?\)""")]
    private static partial Regex User();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex Quoted();
}
