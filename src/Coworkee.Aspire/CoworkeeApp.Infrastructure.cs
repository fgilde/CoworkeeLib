using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.MailPit;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private IResourceBuilder<PostgresServerResource>? _server;
    private IResourceBuilder<PostgresDatabaseResource>? _database;
    private IResourceBuilder<RedisResource>? _redis;
    private IResourceBuilder<MailPitContainerResource>? _mail;
    private IResourceBuilder<ElasticsearchResource>? _search;

    /// <summary>Wait for the server, not the database: the migrations create a missing database, and Aspire 13.6 lost the database state after restarts.</summary>
    public IResourceBuilder<PostgresServerResource> Server => _server ??= AddPostgres();

    public IResourceBuilder<PostgresDatabaseResource> Database => _database ??= Server.AddDatabase(Name);

    public IResourceBuilder<RedisResource> Redis => _redis ??= AddRedis();

    public IResourceBuilder<MailPitContainerResource> Mail => _mail ??= AddMail();

    public IResourceBuilder<ElasticsearchResource> Search => _search ??= AddSearch();

    private IResourceBuilder<IResourceWithConnectionString> AppDatabase => Options.Database ?? Database;

    private EndpointReference Smtp => Options.Smtp ?? Mail.GetEndpoint("smtp");

    private string BlobRoot => Options.BlobRoot ?? Path.GetFullPath(Path.Combine(Builder.AppHostDirectory, "..", "..", ".data", "blobs"));

    private IResourceBuilder<PostgresServerResource> AddPostgres()
    {
        var postgres = Builder.AddPostgres("postgres");
        return Configured(Persistent ? postgres.WithDataVolume() : postgres, Options.Postgres);
    }

    private IResourceBuilder<RedisResource> AddRedis()
    {
        var redis = Builder.AddRedis("redis");
        return Configured(Persistent ? redis.WithDataVolume() : redis, Options.RedisServer);
    }

    private IResourceBuilder<MailPitContainerResource> AddMail() =>
        Configured(Builder.AddMailPit("mail").WithUrlForEndpoint("http", u => u.DisplayText = "Mailpit UI"), Options.MailPit);

    // a small heap keeps development machines responsive and nearly full dev disks do not block shards; the C2 JIT of the bundled JDK crashed (SIGSEGV in PhaseChaitin), so C1 only
    private IResourceBuilder<ElasticsearchResource> AddSearch()
    {
        var search = Builder.AddElasticsearch("elasticsearch")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m -XX:TieredStopAtLevel=1")
            .WithEnvironment("cluster.routing.allocation.disk.threshold_enabled", "false");
        return Configured(Persistent ? search.WithDataVolume() : search, Options.Elasticsearch);
    }

    private static IResourceBuilder<T> Configured<T>(IResourceBuilder<T> resource, Action<IResourceBuilder<T>>? configure)
        where T : IResource
    {
        configure?.Invoke(resource);
        return resource;
    }
}
