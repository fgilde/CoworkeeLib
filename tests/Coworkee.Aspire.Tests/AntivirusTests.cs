using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Tests;

/// <summary>UseAntivirus: the ClamAV container, its address for the services that take uploads, and what a deployment makes of it.</summary>
public sealed class AntivirusTests
{
    [Fact]
    public async Task The_services_with_storage_get_the_address_of_the_clamav_container_and_the_migrations_do_not()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseAntivirus());

        app.AddMigrations<FakeProjects.SeedingMigrations>();
        app.AddAuthServer<StorageAuth>();
        app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();

        var clamav = builder.Resources.OfType<ClamAvResource>().ShouldHaveSingleItem();
        clamav.Name.ShouldBe("clamav");
        clamav.Annotations.OfType<ContainerImageAnnotation>().Single().ShouldSatisfyAllConditions(
            image => image.Image.ShouldBe("clamav/clamav"), image => image.Tag.ShouldBe("1.4"));
        var endpoint = clamav.Annotations.OfType<EndpointAnnotation>().Single();
        (endpoint.Name, endpoint.TargetPort, endpoint.UriScheme, endpoint.IsExternal).ShouldBe(("tcp", 3310, "tcp", false));
        var clamd = await Environment(clamav);
        clamd["CLAMD_CONF_StreamMaxLength"].ShouldBe("2048M"); // clamd's 25 MB default refused larger files
        clamd["CLAMD_CONF_ConcurrentDatabaseReload"].ShouldBe("no");
        clamd["CLAMAV_NO_FRESHCLAMD"].ShouldBe("true"); // the signatures of the image while developing: a faster start, no download

        (await Environment(app.Apis[0].Resource))["ConnectionStrings__antivirus"].ShouldBe("{clamav.connectionString}");
        (await Environment(app.AuthServer!.Resource))["ConnectionStrings__antivirus"].ShouldBe("{clamav.connectionString}"); // registration documents
        (await Environment(app.Migrations!.Resource)).Keys.ShouldNotContain("ConnectionStrings__antivirus");
        (await Environment(app.Web!.Resource)).Keys.ShouldNotContain("ConnectionStrings__antivirus");
        // not awaited: clamd loads its signatures for a minute or two, the api starts meanwhile and refuses uploads until it answers
        app.Apis[0].Resource.Annotations.OfType<WaitAnnotation>().ShouldNotContain(w => w.Resource == clamav);
        ((IResourceWithConnectionString)clamav).ConnectionStringExpression.ValueExpression.ShouldBe("tcp://{clamav.bindings.tcp.host}:{clamav.bindings.tcp.port}");
    }

    [Fact]
    public async Task Without_it_there_is_no_container_and_no_address()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo");

        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();

        builder.Resources.OfType<ClamAvResource>().ShouldBeEmpty();
        (await Environment(app.Apis[0].Resource)).Keys.ShouldNotContain("ConnectionStrings__antivirus");
    }

    [Fact]
    public async Task A_clamd_of_its_own_replaces_the_container()
    {
        var builder = DistributedApplication.CreateBuilder();
        var clamd = builder.AddConnectionString("antivirus");
        var app = builder.AddCoworkeeApp("demo", o => o.UseAntivirus(clamd));

        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();

        builder.Resources.OfType<ClamAvResource>().ShouldBeEmpty();
        (await Environment(app.Apis[0].Resource))["ConnectionStrings__antivirus"].ShouldBe("{antivirus.connectionString}");
    }

    [Fact]
    public async Task A_deployment_updates_the_signatures_and_configure_callbacks_reach_the_container()
    {
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish", "--step", "publish", "--output-path", Directory.CreateTempSubdirectory("coworkee-publish").FullName]);
        var app = builder.AddCoworkeeApp("demo", o => o.UseAntivirus(c => c.WithEnvironment("CLAMD_CONF_MaxScanSize", "4000M")));

        app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();

        var clamd = await Environment(app.ClamAv.Resource);
        clamd.Keys.ShouldNotContain("CLAMAV_NO_FRESHCLAMD");
        clamd["CLAMD_CONF_MaxScanSize"].ShouldBe("4000M");
    }

    [Fact]
    public async Task On_azure_clamav_gets_three_gigabytes_and_tcp_ingress_inside_the_environment()
    {
        var output = Directory.CreateTempSubdirectory("coworkee-publish").FullName;
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish", "--step", "publish", "--output-path", output, $"--{CoworkeeApp.DeployTargetSetting}=azure"]);
        var app = builder.AddCoworkeeApp("demo", o => o.UseAntivirus());
        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();

        using (var host = builder.Build())
        {
            await host.RunAsync(TestContext.Current.CancellationToken);
        }

        var bicep = await File.ReadAllTextAsync(Directory.EnumerateFiles(output, "clamav*.bicep", SearchOption.AllDirectories).Single(), TestContext.Current.CancellationToken);
        bicep.ShouldContain("memory: '3Gi'");
        bicep.ShouldContain("cpu: json('1.5')");
        bicep.ShouldMatch(@"ingress: \{\s*external: false\s*targetPort: 3310\s*transport: 'tcp'");
        bicep.ShouldContain("image: 'docker.io/clamav/clamav:1.4'");
        var api = await File.ReadAllTextAsync(Directory.EnumerateFiles(output, "demo-api*.bicep", SearchOption.AllDirectories).Single(), TestContext.Current.CancellationToken);
        api.ShouldMatch(@"name: 'ConnectionStrings__antivirus'\s*value: 'tcp://clamav:3310'");
    }

    private static async Task<Dictionary<string, string>> Environment(IResourceWithEnvironment resource) =>
        await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

    // an auth server whose registration documents go to the blob storage
    private sealed class StorageAuth() : FakeProjects.FakeProject("Auth", "Coworkee.Infrastructure", "Coworkee.AuthServer", "Coworkee.Account", "Coworkee.Mailing", "Coworkee.Storage");
}
