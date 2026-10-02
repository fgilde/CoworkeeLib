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
    public void Persists_containers_by_default()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddCoworkeeInfrastructure("shareme");

        Container(builder, "postgres").Annotations.OfType<ContainerLifetimeAnnotation>().ShouldHaveSingleItem().Lifetime.ShouldBe(ContainerLifetime.Persistent);
        Container(builder, "postgres").Annotations.OfType<ContainerMountAnnotation>().ShouldNotBeEmpty();
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
