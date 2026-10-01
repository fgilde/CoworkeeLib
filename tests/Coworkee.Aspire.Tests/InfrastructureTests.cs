using Aspire.Hosting;

namespace Coworkee.Aspire.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void Adds_database_cache_and_mail_resources()
    {
        var builder = DistributedApplication.CreateBuilder();

        var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

        builder.Resources.Select(r => r.Name).ShouldBe(["postgres", "shareme", "redis", "mail"], ignoreOrder: true);
        infrastructure.Database.Resource.DatabaseName.ShouldBe("shareme");
    }
}
