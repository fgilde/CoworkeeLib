using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.ClientEntities;

/// <summary>
/// PoC: a set's copy as a SQLite file, built on the server. The rows come from the same in-process OData read as the pages of a download
/// (permission, hidden properties, row filters, navigation scope), all at once by key; SQLite writes them natively in a fraction of what the
/// browser's interpreter needs to parse and insert them. The file goes out gzip-encoded, the browser unpacks it natively.
/// </summary>
internal static class ClientEntitySnapshot
{
    private static readonly DateTimeOffsetToBinaryConverter Binary = new();

    public static async Task<IResult> HandleAsync(string entitySet, ClientEntitySnapshotRequest request, HttpContext http)
    {
        var services = http.RequestServices;
        var entity = services.GetRequiredService<ODataEntityRegistry>().Entities
            .FirstOrDefault(e => string.Equals(e.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase));
        if (entity is not { HasGuidId: true } || services.GetRequiredService<IOptions<ClientEntityOptions>>().Value.Resolve(entity.EntityType) is not { } settings)
        {
            return Results.NotFound();
        }

        if (!await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, http.RequestAborted))
        {
            return Results.Forbid();
        }

        return await (Task<IResult>)typeof(ClientEntitySnapshot).GetMethod(nameof(BuildAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(entity.EntityType).Invoke(null, [http, entity.EntitySet, settings, request])!;
    }

    private static async Task<IResult> BuildAsync<TEntity>(HttpContext http, string entitySet, ClientEntitySettings settings, ClientEntitySnapshotRequest request)
        where TEntity : class
    {
        var services = http.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ClientEntitySnapshot));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var version = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var expand = new HashSet<string>((request.Expand ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        var select = string.Join(",", request.Columns.Where(c => !expand.Contains(c.Name)).Select(c => c.Name));
        var query = $"$select={select}&$orderby=Id" + (expand.Count > 0 ? "&$expand=" + string.Join(",", expand) : "");
        var route = services.GetRequiredService<ODataRoutes>().Default.Prefix;
        var read = await services.GetRequiredService<IODataQueries>().QueryAsync<TEntity>(route, entitySet, query, cancellationToken: http.RequestAborted);
        if (!read.IsSuccess)
        {
            return read.Error!.Kind == Coworkee.Core.Results.ErrorKind.Forbidden ? Results.Forbid() : Results.BadRequest(read.Error.Message);
        }

        var rows = read.Value.Shaped ?? [];
        if (rows.Count > settings.MaxRows)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var queried = watch.ElapsedMilliseconds;
        var path = Path.Combine(Path.GetTempPath(), $"coworkee-snapshot-{Guid.NewGuid():N}.db");
        try
        {
            Write(path, request.Columns, rows, version);
            var written = watch.ElapsedMilliseconds;
            http.Response.ContentType = "application/vnd.sqlite3";
            http.Response.Headers.ContentEncoding = "gzip";
            http.Response.Headers[ClientEntitySnapshotKinds.VersionHeader] = version.ToString("O", CultureInfo.InvariantCulture);
            var size = new FileInfo(path).Length;
            await using (var gzip = new GZipStream(http.Response.Body, CompressionLevel.Fastest, leaveOpen: true))
            await using (var file = File.OpenRead(path))
            {
                await file.CopyToAsync(gzip, http.RequestAborted);
            }

            logger.LogInformation("PoC snapshot of {Set}: {Rows} rows, {Bytes} bytes; read {Read} ms, written {Written} ms, sent {Sent} ms",
                entitySet, rows.Count, size, queried, written - queried, watch.ElapsedMilliseconds - written);
            return Results.Empty;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static void Write(string path, IReadOnlyList<ClientEntitySnapshotColumn> columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, DateTimeOffset version)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        Execute(connection, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;");
        // the table as the client's EF Core model creates it (CopyDb): "Id" the primary key
        Execute(connection, $"CREATE TABLE rows ({string.Join(", ", columns.Select(c => $"\"{c.Name}\" {SqlType(c.Kind)}" + (c.Name == "Id" ? " NOT NULL CONSTRAINT \"PK_rows\" PRIMARY KEY" : "")))})");
        using (var transaction = connection.BeginTransaction())
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO rows ({string.Join(",", columns.Select(c => $"\"{c.Name}\""))}) VALUES ({string.Join(",", columns.Select((_, i) => "$p" + i))})";
            var parameters = columns.Select((_, i) => insert.Parameters.Add(new SqliteParameter("$p" + i, null))).ToList();
            foreach (var row in rows)
            {
                for (var i = 0; i < columns.Count; i++)
                {
                    parameters[i].Value = Value(Find(row, columns[i].Name), columns[i].Kind) ?? DBNull.Value;
                }

                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        foreach (var column in columns.Where(c => c.Indexed && c.Name != "Id"))
        {
            Execute(connection, $"CREATE INDEX \"ix_{column.Name}\" ON rows (\"{column.Name}\")");
        }

        Execute(connection, $"CREATE TABLE meta (version INTEGER NOT NULL); INSERT INTO meta VALUES ({version.UtcTicks.ToString(CultureInfo.InvariantCulture)})");
    }

    private static object? Find(IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) ? value : row.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string SqlType(string kind) => kind switch
    {
        ClientEntitySnapshotKinds.Integer or ClientEntitySnapshotKinds.Boolean or ClientEntitySnapshotKinds.DateTimeOffset => "INTEGER",
        ClientEntitySnapshotKinds.Real => "REAL",
        _ => "TEXT",
    };

    // as EF Core's SQLite provider writes the client's property (CopyModel)
    private static object? Value(object? value, string kind) => value switch
    {
        null => null,
        _ => kind switch
        {
            ClientEntitySnapshotKinds.Guid => (value is Guid guid ? guid.ToString() : value.ToString())!.ToUpperInvariant(),
            ClientEntitySnapshotKinds.Integer => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            ClientEntitySnapshotKinds.Real => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            ClientEntitySnapshotKinds.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? 1L : 0L,
            ClientEntitySnapshotKinds.DateTimeOffset => Binary.ConvertToProvider(value switch
            {
                DateTimeOffset offset => offset,
                DateTime time => new DateTimeOffset(time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time),
                _ => DateTimeOffset.Parse(value.ToString()!, CultureInfo.InvariantCulture),
            }),
            ClientEntitySnapshotKinds.DateTime => (value is DateTimeOffset offset ? offset.UtcDateTime : (DateTime)value).ToString("yyyy\\-MM\\-dd HH\\:mm\\:ss.FFFFFFF", CultureInfo.InvariantCulture),
            ClientEntitySnapshotKinds.Json => JsonSerializer.Serialize(value),
            _ => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString(),
        },
    };

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
