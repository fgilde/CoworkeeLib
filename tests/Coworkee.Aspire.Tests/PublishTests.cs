using Aspire.Hosting;

namespace Coworkee.Aspire.Tests;

public sealed class PublishTests
{
    [Fact]
    public async Task Docker_compose_writes_a_compose_file_with_services_and_containers()
    {
        var output = Directory.CreateTempSubdirectory("coworkee-compose").FullName;
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish", "--step", "publish", "--output-path", output]);
        builder.AddCoworkeeApp("demo", o => o.PublishToDockerCompose()).AddProjects();

        using var app = builder.Build();
        await app.RunAsync(TestContext.Current.CancellationToken);

        var compose = await File.ReadAllTextAsync(Path.Combine(output, "docker-compose.yaml"), TestContext.Current.CancellationToken);
        compose.ShouldContain("demo-web:");
        compose.ShouldContain("postgres:");
        compose.ShouldContain("ConnectionStrings__demo:");
        compose.ShouldContain("service_completed_successfully");
    }

    [Fact]
    public void Runs_without_compose_environment_unless_asked()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddCoworkeeApp("demo");

        builder.Resources.ShouldBeEmpty();
    }
}
