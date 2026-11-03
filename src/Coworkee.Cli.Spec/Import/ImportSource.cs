namespace Coworkee.Cli.Spec.Import;

public enum ImportSource
{
    /// <summary>An existing database: Postgres, SQL Server or SQLite.</summary>
    Db,

    /// <summary>An OpenAPI 3 (or Swagger 2) JSON document.</summary>
    OpenApi,

    /// <summary>A CSV file with a header row.</summary>
    Csv,
}
