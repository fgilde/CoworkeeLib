using System.IO.Compression;
using System.Text.Json;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coworkee.Backup;

/// <summary>Every table of the model as Postgres binary COPY data in one zip, next to a manifest with the migration it fits.</summary>
internal sealed class DatabaseCopy(CoworkeeDbContext db)
{
    private const string ManifestEntry = "manifest.json";

    public async Task<BackupManifest> WriteAsync(Stream zip, CancellationToken cancellationToken)
    {
        var manifest = new BackupManifest(await MigrationAsync(cancellationToken), Tables());
        var connection = await OpenAsync(cancellationToken);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true);
        await using (var entry = archive.CreateEntry(ManifestEntry).Open())
        {
            await JsonSerializer.SerializeAsync(entry, manifest, cancellationToken: cancellationToken);
        }

        foreach (var table in manifest.Tables)
        {
            await using var entry = archive.CreateEntry(table, CompressionLevel.Fastest).Open();
            await using var copy = await connection.BeginRawBinaryCopyAsync($"COPY {table} TO STDOUT (FORMAT BINARY)", cancellationToken);
            await copy.CopyToAsync(entry, cancellationToken);
        }

        return manifest;
    }

    /// <returns>Why the backup does not fit this database, or null after it replaced every table.</returns>
    public async Task<string?> RestoreAsync(Stream zip, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        BackupManifest? manifest;
        await using (var entry = archive.GetEntry(ManifestEntry)?.Open() ?? Stream.Null)
        {
            manifest = entry == Stream.Null ? null : await JsonSerializer.DeserializeAsync<BackupManifest>(entry, cancellationToken: cancellationToken);
        }

        if (manifest is null || !manifest.Tables.ToHashSet().SetEquals(Tables()) || manifest.Tables.Any(t => archive.GetEntry(t) is null))
        {
            return "The backup does not contain the tables of this database.";
        }

        if (manifest.Migration != await MigrationAsync(cancellationToken))
        {
            return $"The backup was made at migration '{manifest.Migration}', the database is at '{await MigrationAsync(cancellationToken)}'.";
        }

        var connection = await OpenAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // ponytail: replica mode skips foreign key triggers while tables load in any order; needs a superuser or the table owner with that right
        await ExecuteAsync(connection, "SET LOCAL session_replication_role = replica", cancellationToken);
        await ExecuteAsync(connection, $"TRUNCATE {string.Join(", ", manifest.Tables)}", cancellationToken);
        foreach (var table in manifest.Tables)
        {
            await using var entry = archive.GetEntry(table)!.Open();
            await using var copy = await connection.BeginRawBinaryCopyAsync($"COPY {table} FROM STDIN (FORMAT BINARY)", cancellationToken);
            await entry.CopyToAsync(copy, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private List<string> Tables() =>
    [
        .. db.Model.GetEntityTypes()
            .Where(t => t.GetTableName() is not null && t.GetViewName() is null && t.ClrType != typeof(DatabaseBackup))
            .Select(t => $"\"{t.GetSchema() ?? "public"}\".\"{t.GetTableName()}\"")
            .Distinct()
            .Order(StringComparer.Ordinal),
    ];

    private async Task<string?> MigrationAsync(CancellationToken cancellationToken) =>
        (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        return (NpgsqlConnection)db.Database.GetDbConnection();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal sealed record BackupManifest(string? Migration, IReadOnlyList<string> Tables);
