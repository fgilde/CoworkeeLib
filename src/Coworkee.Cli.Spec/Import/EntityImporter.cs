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

/// <summary>What to import: <paramref name="Source"/> is a connection string or SQLite file, an OpenAPI file or URL, or a CSV file.</summary>
public sealed record ImportRequest(ImportSource From, string Source, string? Module = null, IReadOnlyCollection<string>? Tables = null, string? Provider = null);

/// <summary>A module with the imported entities, the enums they use and what was left out (one line each).</summary>
public sealed record ImportResult(ModuleSpec Module, IReadOnlyList<EnumSpec> Enums, IReadOnlyList<string> Skipped)
{
    public AppSpec ToSpec(string appName) => new() { Name = appName, Modules = [Module], Enums = [.. Enums] };

    /// <summary>One line per entity: name, properties, references.</summary>
    public IEnumerable<string> Lines() => Module.Entities.Select(e =>
        $"{e.Name} ({string.Join(", ", e.Properties.Select(p => p.Type switch
        {
            PropertyType.Reference => $"{p.Name} -> {p.Reference}",
            PropertyType.Enum => $"{p.Name}: {p.Enum}",
            _ => $"{p.Name}: {p.Type.ToString().ToLowerInvariant()}",
        } + (p.Required ? "!" : string.Empty)))})");
}

public sealed class ImportException(string message) : Exception(message);

/// <summary>Turns a database schema, an OpenAPI document or a CSV file into a module of a spec.</summary>
public static class EntityImporter
{
    public static async Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default) => request.From switch
    {
        ImportSource.Db => await DbImporter.ImportAsync(request, cancellationToken),
        ImportSource.OpenApi => OpenApiImporter.Import(await OpenApiImporter.LoadAsync(request.Source, cancellationToken), request),
        _ => CsvImporter.Import(request),
    };
}
