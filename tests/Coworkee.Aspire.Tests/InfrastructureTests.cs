using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void Adds_database_cache_and_mail_resources()
    {
        var builder = DistributedApplication.CreateBuilder();

        var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

        builder.Resources.Select(r => r.Name).ShouldBe(["postgres", "shareme", "redis", "mail", "elasticsearch"], ignoreOrder: true);
        infrastructure.Database.Resource.DatabaseName.ShouldBe("shareme");
    }

    [Fact]
    public void Keeps_data_in_volumes_by_default_with_session_containers()
    {
        var builder = DistributedApplication.CreateBuilder();

        var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

        foreach (var name in new[] { "postgres", "redis", "elasticsearch" })
        {
            Container(builder, name).Annotations.OfType<ContainerMountAnnotation>().ShouldNotBeEmpty(name);
            Container(builder, name).Annotations.OfType<ContainerLifetimeAnnotation>().ShouldBeEmpty(name);
        }

        infrastructure.Server.Resource.Name.ShouldBe("postgres");
    }

    [Fact]
    public void Ephemeral_switch_skips_volumes_and_persistence()
    {
        var builder = DistributedApplication.CreateBuilder([$"--{CoworkeeInfrastructureExtensions.EphemeralSetting}=true"]);

        builder.AddCoworkeeInfrastructure("shareme");

        foreach (var name in new[] { "postgres", "redis" })
        {
            Container(builder, name).Annotations.OfType<ContainerLifetimeAnnotation>().ShouldBeEmpty();
            Container(builder, name).Annotations.OfType<ContainerMountAnnotation>().ShouldBeEmpty();
        }
    }

    private static IResource Container(IDistributedApplicationBuilder builder, string name) => builder.Resources.Single(r => r.Name == name);
}
