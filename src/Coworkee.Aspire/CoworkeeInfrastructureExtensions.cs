using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.MailPit;
using Microsoft.Extensions.Configuration;

namespace Aspire.Hosting;

/// <param name="Database">The application database; its connection string goes to the services.</param>
/// <param name="Server">The Postgres server: wait for it, not for <paramref name="Database"/>. Aspire 13.6 creates the database through
/// a container exec whose watch can time out on Windows with an existing data volume, and the database then never becomes ready.
/// The migrations create the database when it is missing.</param>
public sealed record CoworkeeInfrastructure(
    IResourceBuilder<PostgresDatabaseResource> Database,
    IResourceBuilder<PostgresServerResource> Server,
    IResourceBuilder<RedisResource> Redis,
    IResourceBuilder<MailPitContainerResource> Mail,
    IResourceBuilder<ElasticsearchResource> Search);

public static class CoworkeeInfrastructureExtensions
{
    public const string EphemeralSetting = "Coworkee:EphemeralInfrastructure";

    public static CoworkeeInfrastructure AddCoworkeeInfrastructure(this IDistributedApplicationBuilder builder, string databaseName)
    {
        var persistent = !builder.Configuration.GetValue<bool>(EphemeralSetting);

        var postgres = builder.AddPostgres("postgres");
        var redis = builder.AddRedis("redis");

        // a small heap keeps development machines responsive and nearly full dev disks do not block shards; the C2 JIT of the bundled JDK crashed (SIGSEGV in PhaseChaitin), so C1 only; production configures its own cluster
        var search = builder.AddElasticsearch("elasticsearch")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m -XX:TieredStopAtLevel=1")
            .WithEnvironment("cluster.routing.allocation.disk.threshold_enabled", "false");
        // data lives in volumes, the containers belong to the session: Aspire 13.6 lost the states of persistent
        // containers after a restart ("watch over ContainerExec terminated") and never started the services
        if (persistent)
        {
            postgres.WithDataVolume();
            redis.WithDataVolume();
            search.WithDataVolume();
        }

        var mail = builder.AddMailPit("mail");
        return new CoworkeeInfrastructure(postgres.AddDatabase(databaseName), postgres, redis, mail, search);
    }
}
