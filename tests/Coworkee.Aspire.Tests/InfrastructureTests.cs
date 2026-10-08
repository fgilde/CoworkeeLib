using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void Keeps_data_in_volumes_by_default_with_session_containers()
    {
        var builder = DistributedApplication.CreateBuilder();

        AddApp(builder);

        foreach (var name in new[] { "postgres", "redis" })
        {
            Container(builder, name).Annotations.OfType<ContainerMountAnnotation>().ShouldNotBeEmpty(name);
            Container(builder, name).Annotations.OfType<ContainerLifetimeAnnotation>().ShouldBeEmpty(name);
        }
    }

    [Fact]
    public void Ephemeral_switch_skips_volumes()
    {
        var builder = DistributedApplication.CreateBuilder([$"--{CoworkeeAppExtensions.EphemeralSetting}=true"]);

        AddApp(builder);

        foreach (var name in new[] { "postgres", "redis" })
        {
            Container(builder, name).Annotations.OfType<ContainerMountAnnotation>().ShouldBeEmpty(name);
        }
    }

    private static void AddApp(IDistributedApplicationBuilder builder)
    {
        var app = builder.AddCoworkeeApp("demo");
        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();
    }

    private static IResource Container(IDistributedApplicationBuilder builder, string name) => builder.Resources.Single(r => r.Name == name);
}
