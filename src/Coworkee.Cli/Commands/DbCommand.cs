using System.CommandLine;
using System.Globalization;
using Coworkee.Cli.Apps;

namespace Coworkee.Cli.Commands;

/// <summary>coworkee db: the local development database of the app host (reset, seed, backup, restore).</summary>
internal static class DbCommand
{
    private static readonly Option<bool> Yes = new("--yes", "-y") { Description = "Do not ask before deleting or overwriting data." };
    private static readonly Option<string?> Volume = new("--volume") { Description = "The data volume, when the app has several (run from several folders)." };
    private static readonly Argument<string?> BackupFile = new("file") { Description = "Target file (default: .data/backups/<name>-<time>.<dump|bak|db>).", Arity = ArgumentArity.ZeroOrOne };
    private static readonly Argument<string> RestoreFile = new("file") { Description = "A backup of coworkee db backup." };

    private const string SqlCmd = "if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then s='/opt/mssql-tools18/bin/sqlcmd -C'; else s=/opt/mssql-tools/bin/sqlcmd; fi; $s -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -b -Q ";
    private const string SqlServerBackup = "/var/opt/mssql/coworkee.bak";

    public static Command Create() => new("db", "The app's local development database: reset, seed, backup and restore.")
    {
        new Command("reset", "Delete the database: the app host's data volume or the SQLite file. The next start migrates and seeds it again.") { Yes, Volume }.Handle(ResetAsync),
        new Command("seed", "Migrate the database and seed the demo users and roles (runs the migrations project against the running database).") { Volume }.Handle(SeedAsync),
        new Command("backup", "Back the database up: pg_dump or BACKUP DATABASE in the running container, or a copy of the SQLite file.") { BackupFile, Volume }
            .Handle((result, cancellationToken) => BackupAsync(Current(), result.GetValue(BackupFile), result.GetValue(Volume), cancellationToken)),
        new Command("restore", "Restore a backup of coworkee db backup; stop the app first for SQLite and SQL Server.") { RestoreFile, Yes, Volume }
            .Handle((result, cancellationToken) => RestoreAsync(Current(), result.GetValue(RestoreFile)!, result.GetValue(Yes), result.GetValue(Volume), cancellationToken)),
    };

    private static async Task<int> ResetAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var database = LocalDatabase.Of(Solution.Find(Environment.CurrentDirectory));
        if (database.IsSqlite)
        {
            if (!Confirm(result.GetValue(Yes), $"Delete {database.SqliteFile}?"))
            {
                return 1;
            }

            foreach (var file in new[] { "", "-wal", "-shm", "-journal" }.Select(s => database.SqliteFile + s).Where(File.Exists))
            {
                File.Delete(file);
            }

            Console.WriteLine("Deleted. The next start creates and seeds the database again.");
            return 0;
        }

        var volume = await VolumeAsync(database, result.GetValue(Volume));
        if (!Confirm(result.GetValue(Yes), $"Delete the volume {volume} with all data of the database?"))
        {
            return 1;
        }

        if (await Shell.RunAsync(["docker", "volume", "rm", volume], cancellationToken: cancellationToken) != 0)
        {
            throw new CliException("The volume is in use: stop the app host (and its containers), then run this again.");
        }

        Console.WriteLine("Deleted. The next start creates and seeds the database again.");
        return 0;
    }

    private static async Task<int> SeedAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var solution = Solution.Find(Environment.CurrentDirectory);
        var database = LocalDatabase.Of(solution);
        string connection;
        if (database.IsSqlite)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(database.SqliteFile)!);
            connection = $"Data Source={database.SqliteFile}";
        }
        else
        {
            var container = await ContainerAsync(database, result.GetValue(Volume));
            var port = await Capture("docker", "port", container, database.Provider == "sqlServer" ? "1433/tcp" : "5432/tcp");
            var password = await Capture("docker", "exec", container, "printenv", database.Provider == "sqlServer" ? "MSSQL_SA_PASSWORD" : "POSTGRES_PASSWORD");
            connection = database.ConnectionString(int.Parse(port.Split('\n')[0].Trim().Split(':')[^1], CultureInfo.InvariantCulture), password);
        }

        return await Shell.RunAsync(["dotnet", "run", "--project", solution.Project("Migrations")], solution.Root, cancellationToken, new Dictionary<string, string>
        {
            [$"ConnectionStrings__{database.Name}"] = connection,
            ["Coworkee__Database__Provider"] = database.ProviderSetting,
            ["DOTNET_ENVIRONMENT"] = "Development",
        });
    }

    private static LocalDatabase Current() => LocalDatabase.Of(Solution.Find(Environment.CurrentDirectory));

    public static async Task<int> BackupAsync(LocalDatabase database, string? target, string? volume, CancellationToken cancellationToken)
    {
        var extension = database.Provider switch { "sqlServer" => "bak", "sqlite" => "db", _ => "dump" };
        var file = Path.GetFullPath(target
            ?? Path.Combine(database.Root, ".data", "backups", $"{database.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}"));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        int exit;
        if (database.IsSqlite)
        {
            File.Copy(File.Exists(database.SqliteFile) ? database.SqliteFile : throw new CliException($"{database.SqliteFile} does not exist yet."), file, overwrite: true);
            exit = 0;
        }
        else
        {
            var container = await ContainerAsync(database, volume);
            exit = database.Provider == "sqlServer"
                ? await SqlServerAsync(container, $"BACKUP DATABASE [{database.Name}] TO DISK = N'{SqlServerBackup}' WITH INIT", cancellationToken) is var code && code == 0
                    ? await Shell.RunAsync(["docker", "cp", $"{container}:{SqlServerBackup}", file], cancellationToken: cancellationToken)
                    : code
                : await Shell.PipeAsync(["docker", "exec", container, "pg_dump", "-U", "postgres", "-Fc", database.Name], null, file, cancellationToken);
        }

        Console.WriteLine(exit == 0 ? $"Backup: {file}" : "The backup failed; see above.");
        return exit;
    }

    public static async Task<int> RestoreAsync(LocalDatabase database, string backup, bool yes, string? volume, CancellationToken cancellationToken)
    {
        var file = Path.GetFullPath(backup);
        if (!File.Exists(file))
        {
            throw new CliException($"{file} does not exist.");
        }

        if (!Confirm(yes, $"Replace the data of the database {database.Name} with {file}?"))
        {
            return 1;
        }

        if (database.IsSqlite)
        {
            foreach (var journal in new[] { "-wal", "-shm", "-journal" }.Select(s => database.SqliteFile + s).Where(File.Exists))
            {
                File.Delete(journal);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(database.SqliteFile)!);
            File.Copy(file, database.SqliteFile, overwrite: true);
            Console.WriteLine("Restored.");
            return 0;
        }

        var container = await ContainerAsync(database, volume);
        var exit = database.Provider == "sqlServer"
            ? await Shell.RunAsync(["docker", "cp", file, $"{container}:{SqlServerBackup}"], cancellationToken: cancellationToken) is var code && code == 0
                ? await SqlServerAsync(container, $"IF DB_ID('{database.Name}') IS NOT NULL ALTER DATABASE [{database.Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [{database.Name}] FROM DISK = N'{SqlServerBackup}' WITH REPLACE; ALTER DATABASE [{database.Name}] SET MULTI_USER", cancellationToken)
                : code
            : await Shell.PipeAsync(["docker", "exec", "-i", container, "pg_restore", "-U", "postgres", "--clean", "--if-exists", "--no-owner", "-d", database.Name], file, null, cancellationToken);
        Console.WriteLine(exit == 0 ? "Restored." : "The restore failed; see above.");
        return exit;
    }

    private static Task<int> SqlServerAsync(string container, string sql, CancellationToken cancellationToken) =>
        Shell.RunAsync(["docker", "exec", container, "sh", "-c", SqlCmd + "\"" + sql + "\""], cancellationToken: cancellationToken);

    private static async Task<string> VolumeAsync(LocalDatabase database, string? chosen)
    {
        if (chosen is not null)
        {
            return chosen;
        }

        var volumes = database.Volumes((await Capture("docker", "volume", "ls", "--format", "{{.Name}}")).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return volumes.Count switch
        {
            0 => throw new CliException($"No {database.Resource} data volume of {database.AppName} found; the app host has not run yet, or it runs without persistent data."),
            1 => volumes[0],
            _ => throw new CliException($"Several data volumes of {database.AppName}; pick one with --volume:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", volumes)}"),
        };
    }

    /// <summary>The running container with the app's data volume.</summary>
    private static async Task<string> ContainerAsync(LocalDatabase database, string? volume)
    {
        var volumes = volume is not null ? [volume] : database.Volumes((await Capture("docker", "volume", "ls", "--format", "{{.Name}}")).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        foreach (var candidate in volumes)
        {
            if ((await Capture("docker", "ps", "--filter", $"volume={candidate}", "--format", "{{.Names}}")).Split('\n')[0].Trim() is { Length: > 0 } container)
            {
                return container;
            }
        }

        throw new CliException($"No running {database.Resource} container of {database.AppName}: start the app (coworkee run) first.");
    }

    private static async Task<string> Capture(params string[] args)
    {
        var (exit, output) = await Shell.CaptureAsync(args);
        return exit == 0 ? output : throw new CliException($"{string.Join(' ', args.Take(3))} failed{(exit == -1 ? ": is Docker installed?" : ".")}");
    }

    private static bool Confirm(bool yes, string question)
    {
        if (yes)
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine($"{question} Pass --yes to confirm.");
            return false;
        }

        Console.Write($"{question} [y/N] ");
        return Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes";
    }
}
