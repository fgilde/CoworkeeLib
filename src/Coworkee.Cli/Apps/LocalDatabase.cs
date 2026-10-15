using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Apps;

/// <summary>
/// The development database the app host starts: a Postgres or SQL Server container with an Aspire data volume
/// (<c>{app}.apphost-{hash}-{postgres|sqlserver}-data</c>) or the SQLite file <c>.data/{name}.db</c>.
/// </summary>
internal sealed partial record LocalDatabase(string Provider, string Name, string Root, string AppName)
{
    public bool IsSqlite => Provider == "sqlite";

    public string SqliteFile => Path.Combine(Root, ".data", Name + ".db");

    /// <summary>The Aspire resource of the server, also the middle of its volume name.</summary>
    public string Resource => Provider == "sqlServer" ? "sqlserver" : "postgres";

    /// <summary>Coworkee:Database:Provider for the migrations project.</summary>
    public string ProviderSetting => Provider switch { "sqlServer" => "SqlServer", "sqlite" => "Sqlite", _ => "Postgres" };

    public static LocalDatabase Of(Solution solution)
    {
        var provider = SpecOptions.Resolve(AppState.Read(solution).Spec)["database"] ?? "postgres";
        var module = Path.Combine(solution.Src, $"{solution.Name}.Infrastructure", $"{solution.Name}InfrastructureModule.cs");
        var name = File.Exists(module) && ConnectionStringName().Match(File.ReadAllText(module)) is { Success: true } match
            ? match.Groups[1].Value
            : solution.Name.ToLowerInvariant();
        return new(provider, name, solution.Root, solution.Name);
    }

    /// <summary>The data volumes of this app's database server among all volumes; several when the app was run from several folders.</summary>
    // ponytail: matched by app name, not by Aspire's hash of the app host path; with several the person picks one with --volume
    public IReadOnlyList<string> Volumes(IEnumerable<string> all) =>
        [.. all.Where(v => v.StartsWith($"{AppName.ToLowerInvariant()}.apphost-", StringComparison.Ordinal) && v.EndsWith($"-{Resource}-data", StringComparison.Ordinal))];

    /// <summary>The connection string to the container's published port.</summary>
    public string ConnectionString(int port, string password) => Provider == "sqlServer"
        ? $"Server=localhost,{port};User ID=sa;Password={password};Database={Name};TrustServerCertificate=true"
        : $"Host=localhost;Port={port};Username=postgres;Password={password};Database={Name}";

    [GeneratedRegex(@"ConnectionStringName = ""(\w+)""")]
    private static partial Regex ConnectionStringName();
}
