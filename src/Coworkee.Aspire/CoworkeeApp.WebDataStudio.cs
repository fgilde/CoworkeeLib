using Aspire.Hosting.ApplicationModel;
using Coworkee.Contracts.Configuration;
using Microsoft.Data.Sqlite;
using Nextended.Aspire.Hosting.WebDataStudio;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    /// <summary>WebDataStudio of <see cref="CoworkeeAppOptions.UseWebDataStudio"/>, in run mode.</summary>
    public IResourceBuilder<WebDataStudioResource>? WebDataStudio { get; private set; }

    private void AddWebDataStudio(Action<IResourceBuilder<WebDataStudioResource>> configure)
    {
        WebDataStudio = Builder.AddWebDataStudio()
            .WithTitle($"{Options.DisplayName ?? Name} data")
            .WithLogin(AdminUser, AdminPassword);
        // the default volume is named after the resource only, so every app on the machine would share saved connections and queries
        foreach (var volume in WebDataStudio.Resource.Annotations.OfType<ContainerMountAnnotation>().Where(m => m.Target == "/data").ToList())
        {
            WebDataStudio.Resource.Annotations.Remove(volume);
        }

        if (Persistent)
        {
            WebDataStudio.WithVolume($"{Name}-webdatastudio-data", "/data");
        }
        var connection = Name.ToUpperInvariant();
        if (Options.Database is { } database)
        {
            WebDataStudio.WithReference(database, connection, Engine(Options.DatabaseProvider));
        }
        else
        {
            AddStudioConnection(WebDataStudio, connection);
        }

        configure(WebDataStudio);
    }

    private void AddStudioConnection(IResourceBuilder<WebDataStudioResource> studio, string connection)
    {
        switch (Options.DatabaseProvider)
        {
            case DatabaseProviders.SqlServer:
                studio.WithReference(SqlServerDatabase, connection, WebDataStudioEngine.SqlServer).WaitFor(SqlServer);
                break;
            case DatabaseProviders.Sqlite:
                Directory.CreateDirectory(Path.GetDirectoryName(SqlitePath)!);
                UseRollbackJournal(SqlitePath);
                studio.WithBindMount(Path.GetDirectoryName(SqlitePath)!, "/data/app")
                    .WithConnection(connection, $"Data Source=/data/app/{Path.GetFileName(SqlitePath)}", WebDataStudioEngine.Sqlite);
                break;
            default:
                studio.WithReference(Database, connection, WebDataStudioEngine.PostgreSql).WaitFor(Server);
                break;
        }
    }

    /// <summary>
    /// The container cannot share the memory-mapped index of a WAL database with the host over a Docker bind mount (SQLite Error 10: disk I/O error),
    /// a rollback journal works. EF Core switches a database it creates to WAL, an existing file it leaves alone, so the file is created or switched here, before the app starts.
    /// </summary>
    private static void UseRollbackJournal(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE";
        command.ExecuteNonQuery();
    }

    private void AddWebDataStudioSignIn(IResourceBuilder<WebDataStudioResource> studio)
    {
        var client = AddKeycloakClient($"{Name}-webdatastudio", [ReferenceExpression.Create($"{studio.GetEndpoint(WebDataStudioResource.HttpEndpointName)}/signin-oidc")]);
        studio.WithSingleSignOn(ContainerKeycloakAuthority(), client.ClientId, client.Secret, Options.Keycloak!.DisplayName, "openid", "profile", "email")
            .WithSignInRoles(admins: [AdminUser, "Administrator"]);
    }

    private static WebDataStudioEngine Engine(string provider) => provider switch
    {
        DatabaseProviders.SqlServer => WebDataStudioEngine.SqlServer,
        DatabaseProviders.Sqlite => WebDataStudioEngine.Sqlite,
        _ => WebDataStudioEngine.PostgreSql,
    };
}
