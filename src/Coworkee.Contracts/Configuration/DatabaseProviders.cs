namespace Coworkee.Contracts.Configuration;

public static class DatabaseProviders
{
    public const string Postgres = "Postgres";
    public const string SqlServer = "SqlServer";
    public const string Sqlite = "Sqlite";

    public static IReadOnlyList<string> All { get; } = [Postgres, SqlServer, Sqlite];

    /// <summary>The provider as written here, whatever the case in the configuration; unknown names throw.</summary>
    public static string Normalize(string? provider) =>
        string.IsNullOrEmpty(provider) ? Postgres
        : All.FirstOrDefault(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase))
          ?? throw new InvalidOperationException($"Unknown database provider '{provider}'; use {string.Join(", ", All)}.");
}
