namespace Coworkee.Cli.Spec.Import;

/// <summary>Tables become entities, columns properties, single-column foreign keys references and Postgres enum types enums.</summary>
public static class DbImporter
{
    private static readonly string[] SystemSchemas = ["pg_catalog", "information_schema", "sys", "pg_toast"];
    private static readonly string[] SystemTables = ["sysdiagrams", "spatial_ref_sys"];

    public static async Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken cancellationToken)
    {
        var (tables, enums) = await DbSchema.ReadAsync(request.Source, request.Provider, cancellationToken);
        return Map(tables, enums, request.Module ?? ModuleName(request.Source), request.Tables);
    }

    public static ImportResult Map(IReadOnlyList<TableInfo> all, IReadOnlyDictionary<string, List<string>> dbEnums, string module, IReadOnlyCollection<string>? only = null)
    {
        var skipped = new List<string>();
        var tables = all.Where(t => !IsSystem(t) && Selected(t, only)).OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
        foreach (var join in tables.Where(IsJoinTable).ToList())
        {
            skipped.Add($"{join.Name}: many-to-many between {string.Join(" and ", join.ForeignKeys.Select(k => k.TargetTable).Order(StringComparer.Ordinal))} is not supported; reference both from an entity of its own instead.");
            tables.Remove(join);
        }

        foreach (var empty in tables.Where(t => !OwnColumns(t).Any()).ToList())
        {
            skipped.Add($"{empty.Name}: no columns besides the ones every entity has.");
            tables.Remove(empty);
        }

        var taken = ImportNames.Set();
        var names = tables.ToDictionary(t => t, t => ImportNames.TypeName(ImportNames.Singular(ImportNames.Pascal(t.Name)), taken));
        var enums = new Dictionary<string, EnumSpec>();
        var entities = tables.Select(t => Entity(t, names, tables, dbEnums, enums, taken, skipped)).ToList();
        return new ImportResult(new ModuleSpec { Name = module, Entities = entities }, [.. enums.Values], skipped);
    }

    private static EntitySpec Entity(TableInfo table, Dictionary<TableInfo, string> names, List<TableInfo> tables, IReadOnlyDictionary<string, List<string>> dbEnums,
        Dictionary<string, EnumSpec> enums, ISet<string> taken, List<string> skipped)
    {
        var name = names[table];
        var plural = ImportNames.Pascal(table.Name);
        var entity = new EntitySpec { Name = name, Plural = plural != name && plural != EntitySpec.Pluralize(name) && SpecValidator.IsSimpleName(plural) ? plural : null };
        var members = ImportNames.Set();
        var keys = table.ForeignKeys.GroupBy(k => k.Name).Where(g => g.Count() == 1).Select(g => g.Single()).ToDictionary(k => k.Column, StringComparer.Ordinal);
        foreach (var multi in table.ForeignKeys.GroupBy(k => k.Name).Where(g => g.Count() > 1))
        {
            skipped.Add($"{table.Name}.{string.Join("+", multi.Select(k => k.Column))}: a foreign key over several columns; the columns stay plain values.");
        }

        var uniques = table.Uniques.Values.Select(c => c.Where(x => !ImportNames.Pascal(x).Equals("TenantId", StringComparison.OrdinalIgnoreCase)).ToList()).Where(c => c.Count == 1).Select(c => c[0]).ToHashSet();
        var dropped = new List<string>();
        foreach (var column in table.Columns)
        {
            var pascal = ImportNames.Pascal(column.Name);
            if (IsSurrogateKey(table, column) || ImportNames.IsBase(pascal))
            {
                dropped.Add(column.Name);
                continue;
            }

            if (keys.TryGetValue(column.Name, out var key) && tables.FirstOrDefault(t => t.Schema == key.TargetSchema && t.Name == key.TargetTable) is { } target)
            {
                if (HasText(target, tables))
                {
                    var reference = pascal.Length > 2 && pascal.EndsWith("Id", StringComparison.Ordinal) ? pascal[..^2] : names[target];
                    entity.Properties.Add(new PropertySpec
                    {
                        Name = ImportNames.Member(reference, name, members, reference: true),
                        Type = PropertyType.Reference,
                        Reference = names[target],
                        Required = !column.Nullable,
                        OnDelete = key.DeleteRule?.ToUpperInvariant() switch { "CASCADE" => DeleteBehavior.Cascade, "SET NULL" when column.Nullable => DeleteBehavior.SetNull, _ => null },
                    });
                    continue;
                }

                skipped.Add($"{table.Name}.{column.Name}: {names[target]} has no text column to show, so the reference stays a plain value.");
            }
            else if (key is not null)
            {
                skipped.Add($"{table.Name}.{column.Name}: references {key.TargetTable}, which is not imported; the column stays a plain value.");
            }

            if (Property(column, pascal, dbEnums, enums, taken) is not { } property)
            {
                skipped.Add($"{table.Name}.{column.Name}: type {column.Type} has no spec type.");
                continue;
            }

            if (table.PrimaryKey.Contains(column.Name))
            {
                property.Required = true;
                property.Unique = table.PrimaryKey.Count == 1 && property.Type is not (PropertyType.Text or PropertyType.Bool);
                skipped.Add($"{table.Name}.{column.Name}: part of the primary key, imported as {(property.Unique ? "unique " : string.Empty)}property; the entity gets its own Id.");
            }
            else
            {
                property.Unique = uniques.Contains(column.Name) && property.Type is not (PropertyType.Text or PropertyType.Bool);
            }

            property.Name = ImportNames.Member(pascal, name, members);
            entity.Properties.Add(property);
        }

        if (dropped.Count > 0)
        {
            skipped.Add($"{table.Name}: {string.Join(", ", dropped)} left out; every entity has Id, tenant and audit columns.");
        }

        return entity;
    }

    private static PropertySpec? Property(ColumnInfo column, string pascal, IReadOnlyDictionary<string, List<string>> dbEnums, Dictionary<string, EnumSpec> enums, ISet<string> taken)
    {
        var property = new PropertySpec { Required = !column.Nullable };
        if (dbEnums.TryGetValue(column.Type, out var values))
        {
            if (!enums.TryGetValue(column.Type, out var spec))
            {
                var labels = ImportNames.Set();
                enums[column.Type] = spec = new EnumSpec { Name = ImportNames.TypeName(ImportNames.Pascal(column.Type), taken), Values = [.. values.Select(v => ImportNames.Unique(ImportNames.Pascal(v), labels))] };
            }

            (property.Type, property.Enum) = (PropertyType.Enum, spec.Name);
            return property;
        }

        if (TypeOf(column.Type.ToLowerInvariant()) is not { } type)
        {
            return null;
        }

        property.Type = type == PropertyType.String ? TextKind(pascal) : type;
        if (type == PropertyType.String && column.MaxLength is > 0)
        {
            property.MaxLength = column.MaxLength;
        }

        if (type == PropertyType.Decimal && column.Precision is >= 1 and <= 38 && column.Scale is null or >= 0 && (column.Scale ?? 0) <= column.Precision)
        {
            (property.Precision, property.Scale) = (column.Precision, column.Scale ?? 0);
        }

        return property;
    }

    // ponytail: the usual type names of Postgres, SQL Server and SQLite; anything else (json, xml, binary, spatial) is reported as skipped
    private static PropertyType? TypeOf(string type) => type switch
    {
        "character varying" or "varchar" or "nvarchar" or "character" or "char" or "nchar" or "bpchar" or "text" or "ntext" or "citext" or "name"
            or "varchar2" or "nvarchar2" or "clob" or "string" => PropertyType.String,
        "integer" or "int" or "int4" or "smallint" or "int2" or "tinyint" or "mediumint" or "serial" or "smallserial" => PropertyType.Int,
        "bigint" or "int8" or "bigserial" => PropertyType.Long,
        "numeric" or "decimal" => PropertyType.Decimal,
        "money" or "smallmoney" => PropertyType.Money,
        "real" or "float" or "float4" or "float8" or "double" or "double precision" => PropertyType.Double,
        "boolean" or "bool" or "bit" => PropertyType.Bool,
        "date" => PropertyType.DateOnly,
        "time" or "time without time zone" or "time with time zone" => PropertyType.TimeOnly,
        "timestamp" or "timestamp without time zone" or "timestamp with time zone" or "timestamptz" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => PropertyType.DateTime,
        "uuid" or "uniqueidentifier" => PropertyType.Guid,
        _ => null,
    };

    private static PropertyType TextKind(string name) =>
        name.Contains("Email", StringComparison.OrdinalIgnoreCase) ? PropertyType.Email
        : name.EndsWith("Url", StringComparison.OrdinalIgnoreCase) || name.Contains("Website", StringComparison.OrdinalIgnoreCase) ? PropertyType.Url
        : PropertyType.String;

    private static bool IsSurrogateKey(TableInfo table, ColumnInfo column) =>
        table.PrimaryKey is [var key] && key == column.Name && TypeOf(column.Type.ToLowerInvariant()) is PropertyType.Int or PropertyType.Long or PropertyType.Guid;

    private static IEnumerable<ColumnInfo> OwnColumns(TableInfo table) =>
        table.Columns.Where(c => !IsSurrogateKey(table, c) && !ImportNames.IsBase(ImportNames.Pascal(c.Name)));

    private static bool HasText(TableInfo table, List<TableInfo> tables) => table.Columns.Any(c =>
        TypeOf(c.Type.ToLowerInvariant()) == PropertyType.String && !table.ForeignKeys.Any(k => k.Column == c.Name) && !IsSurrogateKey(table, c));

    // two foreign keys and nothing else but the key: a link table of a many-to-many relation
    private static bool IsJoinTable(TableInfo table) =>
        table.ForeignKeys.Select(k => k.Name).Distinct().Count() == 2 && table.ForeignKeys.Count == 2
        && table.Columns.All(c => table.ForeignKeys.Any(k => k.Column == c.Name) || table.PrimaryKey.Contains(c.Name) || ImportNames.IsBase(ImportNames.Pascal(c.Name)));

    private static bool IsSystem(TableInfo table) =>
        SystemSchemas.Contains(table.Schema, StringComparer.OrdinalIgnoreCase) || table.Name.StartsWith("__", StringComparison.Ordinal)
        || table.Name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase) || SystemTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase);

    private static bool Selected(TableInfo table, IReadOnlyCollection<string>? only) =>
        only is not { Count: > 0 } || only.Any(o => o.Equals(table.Name, StringComparison.OrdinalIgnoreCase) || o.Equals($"{table.Schema}.{table.Name}", StringComparison.OrdinalIgnoreCase));

    private static string ModuleName(string source)
    {
        var parts = source.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).DistinctBy(p => p[0].Trim().ToLowerInvariant()).ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim(), StringComparer.Ordinal);
        var database = File.Exists(source) ? source : parts.GetValueOrDefault("database") ?? parts.GetValueOrDefault("initial catalog") ?? parts.GetValueOrDefault("data source");
        return database is { Length: > 0 } ? ImportNames.Pascal(Path.GetFileNameWithoutExtension(database)) : "Imported";
    }
}
