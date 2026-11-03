namespace Coworkee.Cli.Spec.Import;

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
