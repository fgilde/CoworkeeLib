using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.MailPit;

namespace Aspire.Hosting;

public sealed partial class CoworkeeAppOptions
{
    internal IResourceBuilder<IResourceWithConnectionString>? Database { get; private set; }

    internal IResourceBuilder<IResourceWithConnectionString>? Redis { get; private set; }

    internal IResourceBuilder<IResourceWithConnectionString>? Search { get; private set; }

    internal EndpointReference? Smtp { get; private set; }

    internal Action<IResourceBuilder<PostgresServerResource>>? Postgres { get; private set; }

    internal Action<IResourceBuilder<RedisResource>>? RedisServer { get; private set; }

    internal Action<IResourceBuilder<MailPitContainerResource>>? MailPit { get; private set; }

    internal Action<IResourceBuilder<ElasticsearchResource>>? Elasticsearch { get; private set; }

    internal Action<IResourceBuilder<KeycloakResource>>? KeycloakServer { get; private set; }

    internal HashSet<string> Disabled { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The app database instead of the Postgres container, e.g. <c>builder.AddConnectionString("myapp")</c> in production; it is not awaited.</summary>
    public CoworkeeAppOptions UseDatabase(IResourceBuilder<IResourceWithConnectionString> database) => Set(() => Database = database);

    /// <summary>Redis for the SignalR backplane instead of the Redis container; it is not awaited.</summary>
    public CoworkeeAppOptions UseRedis(IResourceBuilder<IResourceWithConnectionString> redis) => Set(() => Redis = redis);

    /// <summary>Elasticsearch instead of the Elasticsearch container; it is not awaited.</summary>
    public CoworkeeAppOptions UseSearch(IResourceBuilder<IResourceWithConnectionString> search) => Set(() => Search = search);

    /// <summary>The SMTP endpoint that becomes the default mail server instead of Mailpit.</summary>
    public CoworkeeAppOptions UseMail(EndpointReference smtp) => Set(() => Smtp = smtp);

    public CoworkeeAppOptions ConfigurePostgres(Action<IResourceBuilder<PostgresServerResource>> configure) => Set(() => Postgres += configure);

    public CoworkeeAppOptions ConfigureRedis(Action<IResourceBuilder<RedisResource>> configure) => Set(() => RedisServer += configure);

    public CoworkeeAppOptions ConfigureMail(Action<IResourceBuilder<MailPitContainerResource>> configure) => Set(() => MailPit += configure);

    public CoworkeeAppOptions ConfigureSearch(Action<IResourceBuilder<ElasticsearchResource>> configure) => Set(() => Elasticsearch += configure);

    public CoworkeeAppOptions ConfigureKeycloak(Action<IResourceBuilder<KeycloakResource>> configure) => Set(() => KeycloakServer += configure);

    /// <summary>Switches off the wiring of modules, e.g. <c>Without(CoworkeeModules.Search)</c>: services that use them do not get that infrastructure.</summary>
    public CoworkeeAppOptions Without(params string[] modules) => Set(() => Disabled.UnionWith(modules));

    private CoworkeeAppOptions Set(Action set)
    {
        set();
        return this;
    }
}
