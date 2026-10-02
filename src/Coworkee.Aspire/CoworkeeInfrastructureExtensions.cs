using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.MailPit;
using Microsoft.Extensions.Configuration;

namespace Aspire.Hosting;

public sealed record CoworkeeInfrastructure(
    IResourceBuilder<PostgresDatabaseResource> Database,
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

        // a small heap keeps development machines responsive and nearly full dev disks do not block shards; production configures its own cluster
        var search = builder.AddElasticsearch("elasticsearch")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithEnvironment("cluster.routing.allocation.disk.threshold_enabled", "false");
        if (persistent)
        {
            postgres.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
            redis.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
            search.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
        }

        var mail = builder.AddMailPit("mail");
        return new CoworkeeInfrastructure(postgres.AddDatabase(databaseName), redis, mail, search);
    }
}
