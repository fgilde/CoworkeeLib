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

    internal string DatabaseProvider { get; private set; } = Coworkee.Contracts.Configuration.DatabaseProviders.Postgres;

    internal Action<IResourceBuilder<SqlServerServerResource>>? SqlServerServer { get; private set; }

    internal string? SqlitePath { get; private set; }

    internal Action<IResourceBuilder<RedisResource>>? RedisServer { get; private set; }

    internal Action<IResourceBuilder<MailPitContainerResource>>? MailPit { get; private set; }

    internal Action<IResourceBuilder<ElasticsearchResource>>? Elasticsearch { get; private set; }

    internal Action<IResourceBuilder<KeycloakResource>>? KeycloakServer { get; private set; }

    internal HashSet<string> Disabled { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The app database instead of the Postgres container, e.g. <c>builder.AddConnectionString("myapp")</c> in production; it is not awaited.</summary>
    public CoworkeeAppOptions UseDatabase(IResourceBuilder<IResourceWithConnectionString> database) => Set(() => Database = database);

    /// <summary>SQL Server instead of Postgres: a SQL Server container, or the database of <see cref="UseDatabase"/>. The migrations of the app must be made for SQL Server.</summary>
    public CoworkeeAppOptions UseSqlServer(Action<IResourceBuilder<SqlServerServerResource>>? configure = null) =>
        Set(() => (DatabaseProvider, SqlServerServer) = (Coworkee.Contracts.Configuration.DatabaseProviders.SqlServer, SqlServerServer + configure));

    /// <summary>A SQLite file instead of a database container (default .data/&lt;name&gt;.db next to src), for development and single machine installs.</summary>
    public CoworkeeAppOptions UseSqlite(string? path = null) =>
        Set(() => (DatabaseProvider, SqlitePath) = (Coworkee.Contracts.Configuration.DatabaseProviders.Sqlite, path));

    /// <summary>Redis for the SignalR backplane instead of the Redis container; it is not awaited.</summary>
    public CoworkeeAppOptions UseRedis(IResourceBuilder<IResourceWithConnectionString> redis) => Set(() => Redis = redis);

    /// <summary>No Redis container: the SignalR backplane, presence, cache invalidation and sign-in sessions stay in each process (one instance per service).</summary>
    public CoworkeeAppOptions WithoutRedis() => Without(Coworkee.Aspire.Modules.CoworkeeModules.Realtime);

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
