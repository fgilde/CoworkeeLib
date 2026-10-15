using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Coworkee.Cli.Spec.Import;

public sealed record ColumnInfo(string Name, string Type, bool Nullable, int? MaxLength, int? Precision, int? Scale);

public sealed record ForeignKeyInfo(string Name, string Column, string TargetSchema, string TargetTable, string? DeleteRule);

public sealed class TableInfo(string schema, string name)
{
    public string Schema => schema;

    public string Name => name;

    public List<ColumnInfo> Columns { get; } = [];

    public List<string> PrimaryKey { get; } = [];

    public List<ForeignKeyInfo> ForeignKeys { get; } = [];

    /// <summary>Columns of each unique index, by index name.</summary>
    public Dictionary<string, List<string>> Uniques { get; } = [];
}

/// <summary>Reads tables, columns, keys, unique indexes and (Postgres) enum types through ADO.NET schema queries.</summary>
public static partial class DbSchema
{
    private const string InformationSchemaColumns = """
        select c.table_schema, c.table_name, c.column_name,
          case when c.data_type = 'USER-DEFINED' then c.udt_name else c.data_type end, c.is_nullable,
          c.character_maximum_length, c.numeric_precision, c.numeric_scale
        from information_schema.columns c
        join information_schema.tables t on t.table_schema = c.table_schema and t.table_name = c.table_name
        where t.table_type = 'BASE TABLE' and c.table_schema not in ('pg_catalog', 'information_schema', 'sys')
        order by c.table_schema, c.table_name, c.ordinal_position
        """;

    private const string InformationSchemaKeys = """
        select tc.table_schema, tc.table_name, k.column_name
        from information_schema.table_constraints tc
        join information_schema.key_column_usage k on k.constraint_schema = tc.constraint_schema and k.constraint_name = tc.constraint_name and k.table_name = tc.table_name
        where tc.constraint_type = 'PRIMARY KEY'
        order by tc.table_schema, tc.table_name, k.ordinal_position
        """;

    private const string InformationSchemaForeignKeys = """
        select k.table_schema, k.table_name, k.column_name, u.table_schema, u.table_name, r.delete_rule, r.constraint_name
        from information_schema.referential_constraints r
        join information_schema.key_column_usage k on k.constraint_schema = r.constraint_schema and k.constraint_name = r.constraint_name
        join information_schema.key_column_usage u on u.constraint_schema = r.unique_constraint_schema and u.constraint_name = r.unique_constraint_name
          and u.ordinal_position = k.position_in_unique_constraint
        """;

    private static readonly Dialect Postgres = new(
        cs => new NpgsqlConnection(cs), InformationSchemaColumns, InformationSchemaKeys, InformationSchemaForeignKeys,
        """
        select n.nspname, t.relname, i.relname, a.attname
        from pg_index x join pg_class t on t.oid = x.indrelid join pg_class i on i.oid = x.indexrelid
        join pg_namespace n on n.oid = t.relnamespace join pg_attribute a on a.attrelid = t.oid and a.attnum = any(x.indkey)
        where x.indisunique and not x.indisprimary and n.nspname not in ('pg_catalog', 'information_schema', 'pg_toast')
        """,
        "select t.typname, e.enumlabel from pg_type t join pg_enum e on e.enumtypid = t.oid order by t.typname, e.enumsortorder");

    private static readonly Dialect SqlServer = new(
        cs => new SqlConnection(cs), InformationSchemaColumns, InformationSchemaKeys, InformationSchemaForeignKeys,
        """
        select s.name, t.name, i.name, c.name
        from sys.indexes i join sys.tables t on t.object_id = i.object_id join sys.schemas s on s.schema_id = t.schema_id
        join sys.index_columns ic on ic.object_id = i.object_id and ic.index_id = i.index_id and ic.is_included_column = 0
        join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id
        where i.is_unique = 1 and i.is_primary_key = 0
        """);

    private static readonly Dialect Sqlite = new(
        cs => new SqliteConnection(cs),
        """
        select 'main', m.name, p.name, lower(p.type), case when p."notnull" = 1 or p.pk > 0 then 'NO' else 'YES' end, null, null, null
        from sqlite_master m join pragma_table_info(m.name) p
        where m.type = 'table' and m.name not like 'sqlite_%' order by m.name, p.cid
        """,
        "select 'main', m.name, p.name from sqlite_master m join pragma_table_info(m.name) p where m.type = 'table' and p.pk > 0 order by m.name, p.pk",
        """select 'main', m.name, f."from", 'main', f."table", f.on_delete, m.name || '.' || f.id from sqlite_master m join pragma_foreign_key_list(m.name) f where m.type = 'table'""",
        """
        select 'main', m.name, l.name, i.name
        from sqlite_master m join pragma_index_list(m.name) l join pragma_index_info(l.name) i
        where m.type = 'table' and l."unique" = 1 and l.origin <> 'pk'
        """);

    /// <summary>postgres, sqlServer or sqlite: <paramref name="provider"/> when given, else guessed from the connection string (an existing file is SQLite).</summary>
    public static string Provider(string source, string? provider = null)
    {
        if (provider is { Length: > 0 })
        {
            return provider.ToLowerInvariant() switch
            {
                "postgres" or "postgresql" or "npgsql" => "postgres",
                "sqlserver" or "mssql" => "sqlServer",
                "sqlite" => "sqlite",
                _ => throw new ImportException($"'{provider}' is no database provider; use postgres, sqlServer or sqlite."),
            };
        }

        var text = source.ToLowerInvariant();
        return File.Exists(source) || Path.GetExtension(text) is ".db" or ".sqlite" or ".sqlite3" or ".db3" && !text.Contains('=') ? "sqlite"
            : text.Contains("host=") || text.StartsWith("postgres", StringComparison.Ordinal) ? "postgres"
            : text.Contains("data source=") && !text.Contains("initial catalog=") && !text.Contains("database=") ? "sqlite"
            : "sqlServer";
    }

    public static async Task<(IReadOnlyList<TableInfo> Tables, IReadOnlyDictionary<string, List<string>> Enums)> ReadAsync(string source, string? provider, CancellationToken cancellationToken)
    {
        var kind = Provider(source, provider);
        var dialect = kind switch { "postgres" => Postgres, "sqlServer" => SqlServer, _ => Sqlite };
        var connectionString = kind == "sqlite" && File.Exists(source) ? new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly }.ToString() : source;
        await using var connection = dialect.Connect(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or ArgumentException or InvalidOperationException)
        {
            throw new ImportException($"Cannot connect to the {kind} database: {exception.Message}");
        }

        var tables = new Dictionary<(string, string), TableInfo>();
        TableInfo Table(string schema, string name) => tables.TryGetValue((schema, name), out var table) ? table : tables[(schema, name)] = new TableInfo(schema, name);

        await QueryAsync(connection, dialect.Columns, r => Table(r[0]!, r[1]!).Columns.Add(Column(r)), cancellationToken);
        await QueryAsync(connection, dialect.Keys, r => Table(r[0]!, r[1]!).PrimaryKey.Add(r[2]!), cancellationToken);
        await QueryAsync(connection, dialect.ForeignKeys, r => Table(r[0]!, r[1]!).ForeignKeys.Add(new ForeignKeyInfo(r[6]!, r[2]!, r[3]!, r[4]!, r[5])), cancellationToken);
        await QueryAsync(connection, dialect.Uniques, r =>
        {
            var uniques = Table(r[0]!, r[1]!).Uniques;
            (uniques.TryGetValue(r[2]!, out var columns) ? columns : uniques[r[2]!] = []).Add(r[3]!);
        }, cancellationToken);
        var enums = new Dictionary<string, List<string>>();
        if (dialect.Enums is { } enumQuery)
        {
            await QueryAsync(connection, enumQuery, r => (enums.TryGetValue(r[0]!, out var values) ? values : enums[r[0]!] = []).Add(r[1]!), cancellationToken);
        }

        return ([.. tables.Values.Where(t => t.Columns.Count > 0)], enums);
    }

    private static ColumnInfo Column(string?[] row)
    {
        var type = row[3] ?? string.Empty;
        var size = Size().Match(type);
        int? Number(string? value) => int.TryParse(value, out var number) ? number : null;
        return new ColumnInfo(row[2]!, size.Success ? type[..size.Index].Trim() : type, row[4] == "YES",
            Number(row[5]) ?? (size.Success ? Number(size.Groups[1].Value) : null), Number(row[6]) ?? (size.Success ? Number(size.Groups[1].Value) : null),
            Number(row[7]) ?? (size.Groups[2].Success ? Number(size.Groups[2].Value) : null));
    }

    private static async Task QueryAsync(DbConnection connection, string sql, Action<string?[]> row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            row([.. Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))]);
        }
    }

    // varchar(100), decimal(10, 2)
    [GeneratedRegex(@"\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\)")]
    private static partial Regex Size();

    private sealed record Dialect(Func<string, DbConnection> Connect, string Columns, string Keys, string ForeignKeys, string Uniques, string? Enums = null);
}
