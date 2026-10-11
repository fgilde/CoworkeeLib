using Aspire.Hosting.ApplicationModel;
using Coworkee.Contracts.Configuration;
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
                studio.WithBindMount(Path.GetDirectoryName(SqlitePath)!, "/data/app")
                    .WithConnection(connection, $"Data Source=/data/app/{Path.GetFileName(SqlitePath)}", WebDataStudioEngine.Sqlite);
                break;
            default:
                studio.WithReference(Database, connection, WebDataStudioEngine.PostgreSql).WaitFor(Server);
                break;
        }
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
