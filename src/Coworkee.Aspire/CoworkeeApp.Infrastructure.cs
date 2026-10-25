using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.MailPit;
using Coworkee.Aspire.Settings;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private IResourceBuilder<PostgresServerResource>? _server;
    private IResourceBuilder<PostgresDatabaseResource>? _database;
    private IResourceBuilder<SqlServerServerResource>? _sqlServer;
    private IResourceBuilder<SqlServerDatabaseResource>? _sqlServerDatabase;
    private IResourceBuilder<RedisResource>? _redis;
    private IResourceBuilder<MailPitContainerResource>? _mail;
    private IResourceBuilder<ElasticsearchResource>? _search;

    /// <summary>Wait for the server, not the database: the migrations create a missing database, and Aspire 13.6 lost the database state after restarts.</summary>
    public IResourceBuilder<PostgresServerResource> Server => _server ??= AddPostgres();

    public IResourceBuilder<PostgresDatabaseResource> Database => _database ??= Server.AddDatabase(Name);

    /// <summary>The SQL Server container of <see cref="CoworkeeAppOptions.UseSqlServer"/>; awaited like <see cref="Server"/>.</summary>
    public IResourceBuilder<SqlServerServerResource> SqlServer => _sqlServer ??= Configured(Persistent ? Builder.AddSqlServer("sqlserver").WithDataVolume(DataVolume("sqlserver")) : Builder.AddSqlServer("sqlserver"), Options.SqlServerServer);

    public IResourceBuilder<SqlServerDatabaseResource> SqlServerDatabase => _sqlServerDatabase ??= SqlServer.AddDatabase(Name);

    public IResourceBuilder<RedisResource> Redis => _redis ??= AddRedis();

    public IResourceBuilder<MailPitContainerResource> Mail => _mail ??= AddMail();

    public IResourceBuilder<ElasticsearchResource> Search => _search ??= AddSearch();

    private string SqlitePath => Options.SqlitePath ?? (Persistent
        ? Path.GetFullPath(Path.Combine(Builder.AppHostDirectory, "..", "..", ".data", $"{Name}.db"))
        : Path.Combine(Path.GetTempPath(), $"coworkee-{Name}-{Environment.ProcessId}.db"));

    private EndpointReference Smtp => Options.Smtp ?? Mail.GetEndpoint("smtp");

    private string BlobRoot => Options.BlobRoot ?? Path.GetFullPath(Path.Combine(Builder.AppHostDirectory, "..", "..", ".data", "blobs"));

    /// <summary>The app database, the provider setting and, for a container of this app host, the wait for its server.</summary>
    private IResourceBuilder<ProjectResource> WithAppDatabase(IResourceBuilder<ProjectResource> service, bool awaitServer)
    {
        service.WithSetting(s => s.Coworkee.Database.Provider, Options.DatabaseProvider);
        if (Options.Database is { } database)
        {
            return service.WithReference(database, Name);
        }

        switch (Options.DatabaseProvider)
        {
            case Coworkee.Contracts.Configuration.DatabaseProviders.Sqlite:
                Directory.CreateDirectory(Path.GetDirectoryName(SqlitePath)!);
                return service.WithEnvironment($"ConnectionStrings__{Name}", $"Data Source={SqlitePath}");
            case Coworkee.Contracts.Configuration.DatabaseProviders.SqlServer:
                service.WithReference(SqlServerDatabase, Name);
                return awaitServer ? service.WaitFor(SqlServer) : service;
            default:
                service.WithReference(Database, Name);
                return awaitServer ? service.WaitFor(Server) : service;
        }
    }

    private IResourceBuilder<PostgresServerResource> AddPostgres()
    {
        var postgres = Builder.AddPostgres("postgres");
        return Configured(Persistent ? postgres.WithDataVolume(DataVolume("postgres")) : postgres, Options.Postgres);
    }

    private IResourceBuilder<RedisResource> AddRedis()
    {
        var redis = Builder.AddRedis("redis");
        return Configured(Persistent ? redis.WithDataVolume(DataVolume("redis")) : redis, Options.RedisServer);
    }

    private IResourceBuilder<MailPitContainerResource> AddMail()
    {
        var mail = Builder.AddMailPit("mail").WithUrlForEndpoint("http", u => u.DisplayText = "Mailpit UI");
        if (Publishing)
        {
            // a test deployment's mails: the administrator signs in to the web interface
            ToolUrl(mail);
            mail.WithEnvironment("MP_UI_AUTH", ReferenceExpression.Create($"{AdminEmail}:{AdminPassword}"));
        }

        return Configured(mail, Options.MailPit);
    }

    // a small heap keeps development machines responsive and nearly full dev disks do not block shards; the C2 JIT of the bundled JDK crashed (SIGSEGV in PhaseChaitin), so C1 only
    private IResourceBuilder<ElasticsearchResource> AddSearch()
    {
        var search = Builder.AddElasticsearch("elasticsearch")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m -XX:TieredStopAtLevel=1")
            .WithEnvironment("cluster.routing.allocation.disk.threshold_enabled", "false");
        Configured(Persistent ? search.WithDataVolume(DataVolume("elasticsearch")) : search, Options.Elasticsearch);
        if (Options.KibanaServer is { } kibana && Deploys(CoworkeeTools.Kibana))
        {
            AddKibana(search, kibana);
        }

        return search;
    }

    // Aspire names volumes after the app host's folder; a deployment keeps its data when it is published again from another checkout
    private string? DataVolume(string server) => Publishing ? $"{Name}-{server}-data" : null;

    private static IResourceBuilder<T> Configured<T>(IResourceBuilder<T> resource, Action<IResourceBuilder<T>>? configure)
        where T : IResource
    {
        configure?.Invoke(resource);
        return resource;
    }
}
