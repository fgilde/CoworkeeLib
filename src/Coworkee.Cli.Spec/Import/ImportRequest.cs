namespace Coworkee.Cli.Spec.Import;

/// <summary>What to import: <paramref name="Source"/> is a connection string or SQLite file, an OpenAPI file or URL, or a CSV file.</summary>
public sealed record ImportRequest(ImportSource From, string Source, string? Module = null, IReadOnlyCollection<string>? Tables = null, string? Provider = null);
