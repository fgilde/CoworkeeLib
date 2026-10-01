using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.MailPit;

namespace Aspire.Hosting;

public sealed record CoworkeeInfrastructure(
    IResourceBuilder<PostgresDatabaseResource> Database,
    IResourceBuilder<RedisResource> Redis,
    IResourceBuilder<MailPitContainerResource> Mail);

public static class CoworkeeInfrastructureExtensions
{
    public static CoworkeeInfrastructure AddCoworkeeInfrastructure(this IDistributedApplicationBuilder builder, string databaseName)
    {
        var database = builder.AddPostgres("postgres")
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent)
            .AddDatabase(databaseName);
        var redis = builder.AddRedis("redis")
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent);
        var mail = builder.AddMailPit("mail");
        return new CoworkeeInfrastructure(database, redis, mail);
    }
}
